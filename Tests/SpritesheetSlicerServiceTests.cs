using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NyxAssets.Things;
using NyxAssetsEditor.Services.ImportExport;
using NyxAssetsEditor.Services.Persistence;
using Tomlyn;
using Xunit;

namespace NyxAssetsEditor.Tests;

public class SpritesheetSlicerServiceTests
{
	[Fact]
	public void RemoveOpaqueMagenta_PreservesNonKeyPixels()
	{
		var image = new SlicerImage(3, 1, new byte[]
		{
			255, 0, 255, 255,
			254, 0, 255, 255,
			255, 0, 255, 128
		});

		var normalized = SpritesheetSlicerService.RemoveOpaqueMagenta(image);

		Assert.Equal(new byte[] { 0, 0, 0, 0 }, normalized.Rgba[..4]);
		Assert.Equal(new byte[] { 254, 0, 255, 255 }, normalized.Rgba[4..8]);
		Assert.Equal(new byte[] { 255, 0, 255, 128 }, normalized.Rgba[8..12]);
		Assert.Equal(new byte[] { 255, 0, 255, 255 }, image.Rgba[..4]);
	}

	[Fact]
	public void Slice_PreservesOutfitMaskChannelValues()
	{
		var mask = new byte[]
		{
			255, 255, 0, 255, 128, 0, 0, 255,
			0, 96, 0, 255, 0, 0, 64, 255
		};
		var cells = SpritesheetSlicerService.Slice(
			new SlicerImage(2, 2, mask), new SlicerGrid(0, 0, 1, 1, 2), includeEmpty: true);

		Assert.Equal(mask, cells.Single().Rgba);
	}

	[Theory]
	[InlineData(32, 32, 4)]
	[InlineData(127, 127, 4)]
	[InlineData(128, 128, 2)]
	[InlineData(255, 255, 2)]
	[InlineData(256, 256, 1)]
	[InlineData(64, 256, 1)]
	public void RecommendZoom_UsesPixelSafeLevelsForSmallSheets(int width, int height, double expected)
	{
		Assert.Equal(expected, SpritesheetSlicerService.RecommendZoom(width, height));
	}

	[Fact]
	public void Slice_NormalizesMagentaAndHonorsEmptyOption()
	{
		var pixels = new byte[64 * 32 * 4];
		Fill(pixels, 64, 0, 0, 32, 32, 255, 0, 255, 255);
		Fill(pixels, 64, 32, 0, 32, 32, 10, 20, 30, 255);
		var image = new SlicerImage(64, 32, pixels);

		var withoutEmpty = SpritesheetSlicerService.Slice(image, new SlicerGrid(0, 0, 2, 1, 32), false);
		var withEmpty = SpritesheetSlicerService.Slice(image, new SlicerGrid(0, 0, 2, 1, 32), true);

		Assert.Single(withoutEmpty);
		Assert.Equal(1, withoutEmpty[0].Column);
		Assert.Equal(2, withEmpty.Count);
		Assert.True(withEmpty[0].IsEmpty);
		Assert.All(withEmpty[0].Rgba.Where((_, index) => index % 4 == 3), alpha => Assert.Equal(0, alpha));
	}

	[Fact]
	public void Slice_UsesObjectBuilderColumnThenRowOrder()
	{
		var pixels = new byte[64 * 64 * 4];
		Fill(pixels, 64, 0, 0, 32, 32, 1, 0, 0, 255);
		Fill(pixels, 64, 32, 0, 32, 32, 2, 0, 0, 255);
		Fill(pixels, 64, 0, 32, 32, 32, 3, 0, 0, 255);
		Fill(pixels, 64, 32, 32, 32, 32, 4, 0, 0, 255);

		var cells = SpritesheetSlicerService.Slice(
			new SlicerImage(64, 64, pixels), new SlicerGrid(0, 0, 2, 2, 32), includeEmpty: true);

		Assert.Equal(new[] { (0, 0), (0, 1), (1, 0), (1, 1) }, cells.Select(cell => (cell.Column, cell.Row)));
		Assert.Equal(new byte[] { 1, 3, 2, 4 }, cells.Select(cell => cell.Rgba[0]));
	}

	[Fact]
	public void ClampGrid_NeverAllowsSelectionOutsideImage()
	{
		var result = SpritesheetSlicerService.ClampGrid(new SlicerGrid(50, -5, 4, 9, 32), 96, 64);
		Assert.Equal(new SlicerGrid(0, 0, 3, 2, 32), result);
	}

	[Fact]
	public void RotateAndFlip_PreserveExactPixels()
	{
		var pixels = new byte[]
		{
			1,0,0,255, 2,0,0,255,
			3,0,0,255, 4,0,0,255,
			5,0,0,255, 6,0,0,255
		};
		var image = new SlicerImage(2, 3, pixels);
		var rotated = SpritesheetSlicerService.RotateClockwise(image);
		Assert.Equal((3, 2), (rotated.Width, rotated.Height));
		Assert.Equal(new byte[] { 5, 3, 1, 6, 4, 2 }, RedChannel(rotated));
		Assert.Equal(new byte[] { 2, 1, 4, 3, 6, 5 }, RedChannel(SpritesheetSlicerService.FlipHorizontal(image)));
	}

	[Fact]
	public void DetectGrid_UsesTheCompleteExactGridWithoutTrimmingTransparentCells()
	{
		var pixels = new byte[64 * 64 * 4];
		// Only one interior cell is occupied. The other rows/columns remain structural.
		Fill(pixels, 64, 34, 34, 28, 28, 255, 255, 255, 255);

		var detected = SpritesheetSlicerService.DetectGrid(new SlicerImage(64, 64, pixels), new[] { 32 });
		Assert.True(detected.Success, detected.Message);
		Assert.Equal(32, detected.Grid.CellSize);
		Assert.Equal(2, detected.Grid.Columns);
		Assert.Equal(2, detected.Grid.Rows);
		Assert.Equal(0, detected.Grid.X);
		Assert.Equal(0, detected.Grid.Y);
	}

	[Fact]
	public void DetectGrid_AcceptsAnEntirelyTransparentObjectBuilderSheet()
	{
		var detected = SpritesheetSlicerService.DetectGrid(
			new SlicerImage(128, 96, new byte[128 * 96 * 4]), new[] { 32 });

		Assert.True(detected.Success, detected.Message);
		Assert.Equal(new SlicerGrid(0, 0, 4, 3, 32), detected.Grid);
	}

	[Fact]
	public void DetectGrid_RejectsNonDivisibleImagesInsteadOfGuessingFromTransparency()
	{
		var detected = SpritesheetSlicerService.DetectGrid(
			new SlicerImage(65, 64, new byte[65 * 64 * 4]), new[] { 32 });

		Assert.False(detected.Success);
	}

	[Fact]
	public void ExportPng_DoesNotOverwriteExistingName()
	{
		var directory = Path.Combine(Path.GetTempPath(), $"nyx-slicer-{Guid.NewGuid():N}");
		try
		{
			var rgba = Enumerable.Repeat((byte)255, 32 * 32 * 4).ToArray();
			var first = SpritesheetSlicerService.ExportPng(rgba, 32, directory, "sheet", 1);
			var second = SpritesheetSlicerService.ExportPng(rgba, 32, directory, "sheet", 1);
			Assert.NotEqual(first, second);
			Assert.True(File.Exists(first));
			Assert.True(File.Exists(second));
		}
		finally
		{
			if (Directory.Exists(directory)) Directory.Delete(directory, true);
		}
	}

	[Fact]
	public void SlicerSettings_RoundTripThroughToml()
	{
		var model = new PersistenceService.SettingsTomlModel
		{
			Slicer = new PersistenceService.SlicerStateModel
			{
				WasMaximized = true, LastOpenDirectory = "images", LastExportDirectory = "exports",
				AutoDetectSpriteGrid = false,
				ThingWidth = 2, ThingHeight = 3, ThingLayers = 2,
				ThingPatternX = 4, ThingPatternY = 3, ThingPatternZ = 2, ThingFrames = 6,
				OutfitDirections = 8, OutfitFrames = 4, ThingKind = "Missile", ReplaceExisting = true
			}
		};
		var restored = TomlSerializer.Deserialize<PersistenceService.SettingsTomlModel>(TomlSerializer.Serialize(model));
		Assert.NotNull(restored);
		Assert.True(restored!.Slicer.WasMaximized);
		Assert.False(restored.Slicer.AutoDetectSpriteGrid);
		Assert.Equal(2, restored.Slicer.ThingLayers);
		Assert.Equal(4, restored.Slicer.ThingPatternX);
		Assert.Equal(3, restored.Slicer.ThingPatternY);
		Assert.Equal(2, restored.Slicer.ThingPatternZ);
		Assert.Equal(6, restored.Slicer.ThingFrames);
		Assert.Equal("Missile", restored.Slicer.ThingKind);
		Assert.True(restored.Slicer.ReplaceExisting);
	}

	[Fact]
	public void SlicerSettings_EnableAutomaticGridByDefault()
	{
		var defaults = new PersistenceService.SlicerStateModel();
		Assert.True(defaults.AutoDetectSpriteGrid);
		Assert.Equal(4, defaults.OutfitDirections);
		Assert.Equal(3, defaults.OutfitFrames);
		Assert.Equal(1, defaults.ThingLayers);
		Assert.Equal(1, defaults.ThingPatternX);
		Assert.Equal(1, defaults.ThingPatternY);
		Assert.Equal(1, defaults.ThingPatternZ);
		Assert.Equal(1, defaults.ThingFrames);
	}

	private static void Fill(byte[] pixels, int strideWidth, int x, int y, int width, int height, byte r, byte g, byte b, byte a)
	{
		for (var py = y; py < y + height; py++)
		for (var px = x; px < x + width; px++)
		{
			var index = (py * strideWidth + px) * 4;
			pixels[index] = r; pixels[index + 1] = g; pixels[index + 2] = b; pixels[index + 3] = a;
		}
	}

	private static byte[] RedChannel(SlicerImage image) => image.Rgba.Where((_, index) => index % 4 == 0).ToArray();
}

public class SpritesheetThingBuilderTests
{
	[Fact]
	public void SplitItems_AreLeftToRightTopToBottomAndUseEngineSlotOrder()
	{
		var cells = Cells(4, 2);
		var result = SpritesheetThingBuilder.Build(new SlicerThingBuildRequest(
			ThingKind.Item, new SlicerGrid(0, 0, 4, 2, 32), cells, 100, 200,
			2, 1, 1, 1, 1, 1, 1, 300, true, null, null));

		Assert.Equal(4, result.Things.Count);
		Assert.Equal(new uint[] { 200, 201, 202, 203 }, result.Things.Select(t => t.Id));
		var first = result.Things[0].FrameGroups[0];
		Assert.True(first.TryGetSpriteId(1, 0, 0, 0, 0, 0, 0, out var left));
		Assert.True(first.TryGetSpriteId(0, 0, 0, 0, 0, 0, 0, out var right));
		Assert.Equal((uint)100, left);
		Assert.Equal((uint)101, right);
	}

	[Fact]
	public void Outfit_MapsDirectionsAndFramesThroughFrameGroupIndexing()
	{
		var cells = Cells(8, 6);
		var result = SpritesheetThingBuilder.Build(new SlicerThingBuildRequest(
			ThingKind.Outfit, new SlicerGrid(0, 0, 8, 6, 32), cells, 500, 50,
			0, 0, 1, 4, 1, 1, 3, 275, true, null, null));
		var group = result.Things.Single().FrameGroups.Single();

		Assert.Equal((uint)2, group.Width);
		Assert.Equal((uint)2, group.Height);
		Assert.Equal((uint)4, group.PatternX);
		Assert.Equal((uint)3, group.Frames);
		Assert.Equal(3, group.FrameTimings!.Length);
		Assert.True(group.TryGetSpriteId(1, 1, 0, 2, 0, 0, 1, out var topLeft));
		Assert.Equal((uint)(500 + 1 * 2 * 8 + 2 * 2), topLeft);
	}

	[Fact]
	public void ClassicThreeFrameOutfit_SplitsIdleAndWalkingForFrameGroupTargets()
	{
		var result = SpritesheetThingBuilder.Build(new SlicerThingBuildRequest(
			ThingKind.Outfit, new SlicerGrid(0, 0, 4, 3, 32), Cells(4, 3), 100, 50,
			0, 0, 1, 4, 1, 1, 3, 275, true, null, null, OutfitFrameGroups: true));
		var thing = result.Things.Single();

		Assert.Equal(2, thing.FrameGroups.Count);
		Assert.Equal(0, (int)thing.FrameGroups[0].GroupTypeId);
		Assert.Equal((uint)1, thing.FrameGroups[0].Frames);
		Assert.False(thing.FrameGroups[0].IsAnimation);
		Assert.Equal(new uint[] { 100, 101, 102, 103 }, thing.FrameGroups[0].SpriteIds);
		Assert.Equal(1, (int)thing.FrameGroups[1].GroupTypeId);
		Assert.Equal((uint)2, thing.FrameGroups[1].Frames);
		Assert.True(thing.FrameGroups[1].IsAnimation);
		Assert.Equal(new uint[] { 104, 105, 106, 107, 108, 109, 110, 111 }, thing.FrameGroups[1].SpriteIds);
	}

	[Fact]
	public void ClassicThreeFrameOutfit_RemainsOneGroupForLegacyTargets()
	{
		var result = SpritesheetThingBuilder.Build(new SlicerThingBuildRequest(
			ThingKind.Outfit, new SlicerGrid(0, 0, 4, 3, 32), Cells(4, 3), 100, 50,
			0, 0, 1, 4, 1, 1, 3, 275, true, null, null, OutfitFrameGroups: false));

		Assert.Single(result.Things.Single().FrameGroups);
		Assert.Equal((uint)3, result.Things.Single().FrameGroups[0].Frames);
	}

	[Fact]
	public void Replacement_PreservesFlagsAndIdButReplacesFrameGroups()
	{
		var replacement = new ThingType { Id = 77, Kind = ThingKind.Item, IsGround = true, GroundSpeed = 180 };
		replacement.FrameGroups.Add(new ThingFrameGroup { Width = 1, Height = 1, Layers = 1, PatternX = 1, PatternY = 1, PatternZ = 1, Frames = 1, SpriteIds = new uint[] { 9 } });
		var result = SpritesheetThingBuilder.Build(new SlicerThingBuildRequest(
			ThingKind.Item, new SlicerGrid(0, 0, 1, 1, 32), Cells(1, 1), 900, 77,
			0, 0, 1, 1, 1, 1, 1, 300, true, null, replacement));
		var thing = result.Things.Single();

		Assert.Equal((uint)77, thing.Id);
		Assert.True(thing.IsGround);
		Assert.Equal((uint)180, thing.GroundSpeed);
		Assert.Equal((uint)900, thing.FrameGroups.Single().SpriteIds.Single());
	}

	[Fact]
	public void Replacement_RejectsSplitSelections()
	{
		var replacement = new ThingType { Id = 7, Kind = ThingKind.Effect };
		Assert.Throws<InvalidOperationException>(() => SpritesheetThingBuilder.Build(new SlicerThingBuildRequest(
			ThingKind.Effect, new SlicerGrid(0, 0, 2, 1, 32), Cells(2, 1), 1, 7,
			1, 1, 1, 1, 1, 1, 1, 100, true, null, replacement)));
	}

	[Fact]
	public void EffectTemplate_PreservesDefinitionAndReplacesFrameGroups()
	{
		var template = new ThingType { Id = 12, Kind = ThingKind.Effect, HasLight = true, LightLevel = 7 };
		template.FrameGroups.Add(new ThingFrameGroup { Width = 1, Height = 1, Layers = 1, PatternX = 1, PatternY = 1, PatternZ = 1, Frames = 1, SpriteIds = new uint[] { 44 } });

		var result = SpritesheetThingBuilder.Build(new SlicerThingBuildRequest(
			ThingKind.Effect, new SlicerGrid(0, 0, 1, 1, 32), Cells(1, 1), 800, 20,
			0, 0, 1, 1, 1, 1, 1, 100, true, template, null));
		var thing = result.Things.Single();

		Assert.Equal((uint)20, thing.Id);
		Assert.True(thing.HasLight);
		Assert.Equal((uint)7, thing.LightLevel);
		Assert.Equal((uint)800, thing.FrameGroups.Single().SpriteIds.Single());
	}

	[Fact]
	public void OutfitTemplate_PreservesDefinitionAndUsesConfiguredLayout()
	{
		var template = new ThingType { Id = 30, Kind = ThingKind.Outfit, HasLight = true, LightLevel = 9 };
		template.FrameGroups.Add(new ThingFrameGroup { Width = 1, Height = 1, Layers = 1, PatternX = 1, PatternY = 1, PatternZ = 1, Frames = 1, SpriteIds = new uint[] { 55 } });

		var result = SpritesheetThingBuilder.Build(new SlicerThingBuildRequest(
			ThingKind.Outfit, new SlicerGrid(0, 0, 4, 3, 32), Cells(4, 3), 900, 40,
			0, 0, 1, 4, 1, 1, 3, 250, true, template, null));
		var thing = result.Things.Single();
		var group = thing.FrameGroups.Single();

		Assert.Equal((uint)40, thing.Id);
		Assert.True(thing.HasLight);
		Assert.Equal((uint)9, thing.LightLevel);
		Assert.Equal((uint)4, group.PatternX);
		Assert.Equal((uint)3, group.Frames);
		Assert.DoesNotContain((uint)55, group.SpriteIds);
	}

	[Fact]
	public void Outfit_PreservesRecolourAddonAndMountedPoseAxes()
	{
		// 4 directions, 2 sprite layers, 3 PatternY entries (body + two addons),
		// and 2 PatternZ entries (normal + mounted rider) occupy 16 x 3 cells.
		var result = SpritesheetThingBuilder.Build(new SlicerThingBuildRequest(
			ThingKind.Outfit, new SlicerGrid(0, 0, 16, 3, 32), Cells(16, 3), 1000, 60,
			0, 0, 2, 4, 3, 2, 1, 250, true, null, null));
		var group = result.Things.Single().FrameGroups.Single();

		Assert.Equal((uint)2, group.Layers);
		Assert.Equal((uint)4, group.PatternX);
		Assert.Equal((uint)3, group.PatternY);
		Assert.Equal((uint)2, group.PatternZ);
		Assert.True(group.TryGetSpriteId(0, 0, 1, 3, 2, 1, 0, out var lastMaskSlot));
		Assert.Equal((uint)1047, lastMaskSlot);
	}

	[Theory]
	[InlineData(ThingKind.Effect)]
	[InlineData(ThingKind.Missile)]
	public void EffectAndMissile_UseTheSharedSplitPipeline(ThingKind kind)
	{
		var result = SpritesheetThingBuilder.Build(new SlicerThingBuildRequest(
			kind, new SlicerGrid(0, 0, 2, 2, 32), Cells(2, 2), 30, 10,
			1, 1, 1, 1, 1, 1, 1, 100, true, null, null));
		Assert.Equal(4, result.Things.Count);
		Assert.All(result.Things, thing =>
		{
			Assert.Equal(kind, thing.Kind);
			Assert.Equal((uint)1, thing.FrameGroups.Single().Width);
			Assert.Equal((uint)1, thing.FrameGroups.Single().Height);
		});
	}

	[Fact]
	public void Missile_MapsObjectBuilderPatternGridAndPreservesEmptyCenterSlot()
	{
		var cells = Cells(3, 3).ToList();
		var emptyPixels = new byte[32 * 32 * 4];
		cells[cells.FindIndex(cell => cell.Column == 1 && cell.Row == 1)] = new SlicerCell(1, 1, emptyPixels, true);

		var result = SpritesheetThingBuilder.Build(new SlicerThingBuildRequest(
			ThingKind.Missile, new SlicerGrid(0, 0, 3, 3, 32), cells, 100, 25,
			0, 0, 1, 3, 3, 1, 1, 500, true, null, null));
		var group = result.Things.Single().FrameGroups.Single();

		Assert.Equal((uint)3, group.PatternX);
		Assert.Equal((uint)3, group.PatternY);
		Assert.True(group.TryGetSpriteId(0, 0, 0, 1, 1, 0, 0, out var center));
		Assert.Equal((uint)0, center);
		Assert.Equal(8, result.SpritePixels.Count);
	}

	[Fact]
	public void GenericFrameGroup_UsesObjectBuilderTextureAndInnerCoordinateOrder()
	{
		var result = SpritesheetThingBuilder.Build(new SlicerThingBuildRequest(
			ThingKind.Item, new SlicerGrid(0, 0, 8, 8, 32), Cells(8, 8), 1000, 70,
			2, 2, 2, 2, 2, 1, 2, 400, true, null, null));
		var group = result.Things.Single().FrameGroups.Single();

		Assert.Equal((uint)2, group.Width);
		Assert.Equal((uint)2, group.Height);
		Assert.Equal((uint)2, group.Layers);
		Assert.Equal((uint)2, group.PatternX);
		Assert.Equal((uint)2, group.PatternY);
		Assert.Equal((uint)1, group.PatternZ);
		Assert.Equal((uint)2, group.Frames);
		Assert.Equal(2, group.FrameTimings!.Length);
		Assert.All(group.FrameTimings, timing => Assert.Equal((uint)400, timing.MinimumMilliseconds));
		Assert.True(group.TryGetSpriteId(1, 1, 1, 1, 0, 0, 0, out var firstTextureTopLeft));
		Assert.Equal((uint)1006, firstTextureTopLeft);
	}

	[Fact]
	public void CombinedObjectBuilderSheet_PreservesDefaultAndWalkingGroupsFromTemplate()
	{
		var template = new ThingType { Id = 90, Kind = ThingKind.Outfit };
		template.FrameGroups.Add(new ThingFrameGroup
		{
			GroupTypeId = 0, Width = 1, Height = 1, Layers = 1,
			PatternX = 1, PatternY = 1, PatternZ = 1, Frames = 1, SpriteIds = new uint[1]
		});
		template.FrameGroups.Add(new ThingFrameGroup
		{
			GroupTypeId = 1, Width = 1, Height = 1, Layers = 1,
			PatternX = 4, PatternY = 1, PatternZ = 1, Frames = 2, SpriteIds = new uint[8]
		});
		var cells = Cells(4, 3).ToList();
		for (var column = 1; column < 4; column++)
			cells[cells.FindIndex(cell => cell.Column == column && cell.Row == 0)] =
				new SlicerCell(column, 0, new byte[32 * 32 * 4], true);

		var result = SpritesheetThingBuilder.Build(new SlicerThingBuildRequest(
			ThingKind.Outfit, new SlicerGrid(0, 0, 4, 3, 32), cells, 100, 200,
			0, 0, 1, 4, 1, 1, 1, 300, true, template, null, OutfitFrameGroups: true));
		var thing = result.Things.Single();

		Assert.Equal(2, thing.FrameGroups.Count);
		Assert.Equal(0, (int)thing.FrameGroups[0].GroupTypeId);
		Assert.Equal(1, (int)thing.FrameGroups[1].GroupTypeId);
		Assert.Equal(9, result.SpritePixels.Count);
		Assert.True(thing.FrameGroups[0].TryGetSpriteId(0, 0, 0, 0, 0, 0, 0, out var idle));
		Assert.Equal((uint)100, idle);
		Assert.True(thing.FrameGroups[1].TryGetSpriteId(0, 0, 0, 3, 0, 0, 1, out var lastWalking));
		Assert.Equal((uint)108, lastWalking);

		var legacyResult = SpritesheetThingBuilder.Build(new SlicerThingBuildRequest(
			ThingKind.Outfit, new SlicerGrid(0, 0, 4, 3, 32), cells, 200, 201,
			0, 0, 1, 4, 1, 1, 1, 300, false, template, null, OutfitFrameGroups: false));
		var legacyGroup = legacyResult.Things.Single().FrameGroups.Single();
		Assert.Equal((uint)3, legacyGroup.Frames);
		Assert.Equal(9, legacyGroup.SpriteIds.Length);
	}

	private static IReadOnlyList<SlicerCell> Cells(int columns, int rows)
	{
		var result = new System.Collections.Generic.List<SlicerCell>();
		for (var row = 0; row < rows; row++)
		for (var column = 0; column < columns; column++)
		{
			var pixels = new byte[32 * 32 * 4]; pixels[3] = 255;
			result.Add(new SlicerCell(column, row, pixels, false));
		}
		return result;
	}
}
