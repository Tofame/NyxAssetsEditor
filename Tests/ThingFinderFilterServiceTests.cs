using System.Linq;
using NyxAssets.Things;
using NyxAssetsEditor.Services.Things;
using Xunit;

namespace NyxAssetsEditor.Tests;

public sealed class ThingFinderFilterServiceTests
{
	[Fact]
	public void EmptyCriteriaReturnsOnlySelectedKindInIdOrder()
	{
		var things = new[] { Thing(9, ThingKind.Outfit), Thing(101, ThingKind.Item), Thing(100, ThingKind.Item) };

		var result = ThingFinderFilterService.Filter(things, ThingKind.Item, [], 0);

		Assert.Equal(new uint[] { 100, 101 }, result.Select(thing => thing.Id));
	}

	[Fact]
	public void UntouchedControlsDoNotCreateCriteria()
	{
		var boolean = new ThingFinderFieldDescriptor(nameof(ThingType.IsGround), "Is Ground",
			ThingFinderFieldSource.Thing, ThingFinderValueKind.Boolean);
		var numeric = new ThingFinderFieldDescriptor(nameof(ThingType.GroundSpeed), "Ground Speed",
			ThingFinderFieldSource.Thing, ThingFinderValueKind.Number);

		Assert.Null(ThingFinderFilterService.CreateCriterion(boolean, false, false, null));
		Assert.Null(ThingFinderFilterService.CreateCriterion(numeric, false, false, "123"));
		Assert.Null(ThingFinderFilterService.CreateCriterion(numeric, true, false, "  "));
	}

	[Fact]
	public void EnabledBooleanFieldsAndCustomFlagsCreateExpectedCriteria()
	{
		var boolean = new ThingFinderFieldDescriptor(nameof(ThingType.IsGround), "Is Ground",
			ThingFinderFieldSource.Thing, ThingFinderValueKind.Boolean);
		var customFlag = new ThingFinderFieldDescriptor("customFlag", "Custom Flag",
			ThingFinderFieldSource.ExtraProperty, ThingFinderValueKind.Boolean);

		Assert.Equal(ThingFinderOperator.IsSet,
			ThingFinderFilterService.CreateCriterion(boolean, true, true, null)!.Operator);
		Assert.Equal(ThingFinderOperator.IsNotSet,
			ThingFinderFilterService.CreateCriterion(boolean, true, false, null)!.Operator);
		Assert.Equal(ThingFinderOperator.Exists,
			ThingFinderFilterService.CreateCriterion(customFlag, true, true, null)!.Operator);
		Assert.Equal(ThingFinderOperator.Missing,
			ThingFinderFilterService.CreateCriterion(customFlag, true, false, null)!.Operator);
	}

	[Fact]
	public void NumericDescriptorsUseEditorRangesAndWholeNumbers()
	{
		var descriptors = ThingFinderFilterService.GetFieldDescriptors()
			.ToDictionary(descriptor => descriptor.Key);

		Assert.Equal(new ThingFinderNumericMetadata(0, 215, 1, 0),
			descriptors[nameof(ThingType.LightColor)].Numeric);
		Assert.Equal(new ThingFinderNumericMetadata(-1024, 1024, 1, 0),
			descriptors[nameof(ThingType.OffsetX)].Numeric);
		Assert.Equal(new ThingFinderNumericMetadata(1, 32, 1, 1),
			descriptors[nameof(ThingFrameGroup.PatternX)].Numeric);
		Assert.Equal(new ThingFinderNumericMetadata(1, 60, 1, 1),
			descriptors[nameof(ThingFrameGroup.Frames)].Numeric);
		Assert.False(descriptors[nameof(ThingFrameGroup.PatternX)].Numeric!.AllowsDecimal);
		Assert.False(descriptors[nameof(ThingFrameGroup.Frames)].Numeric!.AllowsDecimal);
	}

	[Fact]
	public void ActiveCriteriaAreCombined()
	{
		var matching = Thing(100, ThingKind.Item); matching.IsGround = true; matching.GroundSpeed = 150;
		var wrongSpeed = Thing(101, ThingKind.Item); wrongSpeed.IsGround = true; wrongSpeed.GroundSpeed = 80;
		var wrongFlag = Thing(102, ThingKind.Item); wrongFlag.IsGround = false; wrongFlag.GroundSpeed = 150;
		var criteria = new[]
		{
			Criterion(nameof(ThingType.IsGround), ThingFinderOperator.IsSet),
			Criterion(nameof(ThingType.GroundSpeed), ThingFinderOperator.Equals, "150"),
		};

		var result = ThingFinderFilterService.Filter(new[] { matching, wrongSpeed, wrongFlag }, ThingKind.Item, criteria, 0);

		Assert.Equal(new uint[] { 100 }, result.Select(thing => thing.Id));
	}

	[Fact]
	public void BooleanCriteriaMatchTrueAndFalse()
	{
		var ground = Thing(100, ThingKind.Item); ground.IsGround = true;
		var decoration = Thing(101, ThingKind.Item); decoration.IsGround = false;

		Assert.True(ThingFinderFilterService.Matches(ground, Criterion(nameof(ThingType.IsGround), ThingFinderOperator.IsSet), 0));
		Assert.True(ThingFinderFilterService.Matches(decoration, Criterion(nameof(ThingType.IsGround), ThingFinderOperator.IsNotSet), 0));
		Assert.False(ThingFinderFilterService.Matches(ground, Criterion(nameof(ThingType.IsGround), ThingFinderOperator.IsNotSet), 0));
	}

	[Fact]
	public void TextEqualityIsCaseInsensitive()
	{
		var sword = Thing(100, ThingKind.Item); sword.MarketName = "Knight Sword";
		var shield = Thing(101, ThingKind.Item); shield.MarketName = "Tower Shield";

		var result = ThingFinderFilterService.Filter(
			new[] { sword, shield }, ThingKind.Item,
			new[] { Criterion(nameof(ThingType.MarketName), ThingFinderOperator.Equals, "KNIGHT SWORD") }, 0);

		Assert.Equal(new uint[] { 100 }, result.Select(thing => thing.Id));
	}

	[Fact]
	public void EnumValuesCanBeMatchedByName()
	{
		var item = Thing(100, ThingKind.Item);
		var outfit = Thing(1, ThingKind.Outfit);
		var criterion = Criterion(nameof(ThingType.Kind), ThingFinderOperator.Equals, "Outfit");

		Assert.False(ThingFinderFilterService.Matches(item, criterion, 0));
		Assert.True(ThingFinderFilterService.Matches(outfit, criterion, 0));
	}

	[Fact]
	public void ExtraPropertiesSupportExistenceAndAbsence()
	{
		var door = Thing(100, ThingKind.Item); door.ExtraProperties["article"] = "a";
		var floor = Thing(101, ThingKind.Item);

		Assert.True(ThingFinderFilterService.Matches(door, Extra("article", ThingFinderOperator.Exists), 0));
		Assert.True(ThingFinderFilterService.Matches(floor, Extra("article", ThingFinderOperator.Missing), 0));
		Assert.False(ThingFinderFilterService.Matches(floor, Extra("article", ThingFinderOperator.Exists), 0));
	}

	[Fact]
	public void PatternCriteriaUseChosenFrameGroupAndRejectMissingGroup()
	{
		var outfit = Thing(1, ThingKind.Outfit);
		outfit.FrameGroups[0].PatternX = 4;
		outfit.FrameGroups.Add(new ThingFrameGroup { PatternX = 8, PatternY = 3, SpriteIds = new uint[24] });
		var patternX = new ThingFinderCriterion
		{
			Source = ThingFinderFieldSource.Pattern,
			FieldName = nameof(ThingFrameGroup.PatternX),
			Operator = ThingFinderOperator.Equals,
			Value = "8",
		};

		Assert.False(ThingFinderFilterService.Matches(outfit, patternX, 0));
		Assert.True(ThingFinderFilterService.Matches(outfit, patternX, 1));
		Assert.False(ThingFinderFilterService.Matches(outfit, patternX, 2));
	}

	private static ThingType Thing(uint id, ThingKind kind)
	{
		var thing = new ThingType { Id = id, Kind = kind };
		thing.FrameGroups.Add(new ThingFrameGroup { SpriteIds = new uint[1] });
		return thing;
	}

	private static ThingFinderCriterion Criterion(string field, ThingFinderOperator op, string value = "") => new()
	{
		Source = ThingFinderFieldSource.Thing,
		FieldName = field,
		Operator = op,
		Value = value,
	};

	private static ThingFinderCriterion Extra(string key, ThingFinderOperator op) => new()
	{
		Source = ThingFinderFieldSource.ExtraProperty,
		FieldName = key,
		Operator = op,
	};
}
