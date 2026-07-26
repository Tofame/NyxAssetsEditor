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
	public void DetectGrid_UsesTransparentSeparatorsWithoutCropping()
	{
		var pixels = new byte[64 * 64 * 4];
		Fill(pixels, 64, 2, 2, 28, 28, 255, 255, 255, 255);
		Fill(pixels, 64, 34, 2, 28, 28, 255, 255, 255, 255);
		Fill(pixels, 64, 2, 34, 28, 28, 255, 255, 255, 255);
		Fill(pixels, 64, 34, 34, 28, 28, 255, 255, 255, 255);

		var detected = SpritesheetSlicerService.DetectGrid(new SlicerImage(64, 64, pixels), new[] { 32 });
		Assert.True(detected.Success, detected.Message);
		Assert.Equal(32, detected.Grid.CellSize);
		Assert.Equal(2, detected.Grid.Columns);
		Assert.Equal(2, detected.Grid.Rows);
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
				Subdivisions = true, IncludeEmptySprites = false, ThingWidth = 2, ThingHeight = 3,
				TemplateItemId = 77, OutfitDirections = 8, OutfitFrames = 4, ThingKind = "Missile", ReplaceExisting = true
			}
		};
		var restored = TomlSerializer.Deserialize<PersistenceService.SettingsTomlModel>(TomlSerializer.Serialize(model));
		Assert.NotNull(restored);
		Assert.True(restored!.Slicer.WasMaximized);
		Assert.Equal((uint)77, restored.Slicer.TemplateItemId);
		Assert.Equal("Missile", restored.Slicer.ThingKind);
		Assert.True(restored.Slicer.ReplaceExisting);
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
			2, 1, 4, 3, 300, true, null, null));

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
			0, 0, 4, 3, 275, true, null, null));
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
	public void Replacement_PreservesFlagsAndIdButReplacesFrameGroups()
	{
		var replacement = new ThingType { Id = 77, Kind = ThingKind.Item, IsGround = true, GroundSpeed = 180 };
		replacement.FrameGroups.Add(new ThingFrameGroup { Width = 1, Height = 1, Layers = 1, PatternX = 1, PatternY = 1, PatternZ = 1, Frames = 1, SpriteIds = new uint[] { 9 } });
		var result = SpritesheetThingBuilder.Build(new SlicerThingBuildRequest(
			ThingKind.Item, new SlicerGrid(0, 0, 1, 1, 32), Cells(1, 1), 900, 77,
			0, 0, 4, 3, 300, true, null, replacement));
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
			1, 1, 4, 3, 100, true, null, replacement)));
	}

	[Theory]
	[InlineData(ThingKind.Effect)]
	[InlineData(ThingKind.Missile)]
	public void EffectAndMissile_UseTheSharedSplitPipeline(ThingKind kind)
	{
		var result = SpritesheetThingBuilder.Build(new SlicerThingBuildRequest(
			kind, new SlicerGrid(0, 0, 2, 2, 32), Cells(2, 2), 30, 10,
			1, 1, 4, 3, 100, true, null, null));
		Assert.Equal(4, result.Things.Count);
		Assert.All(result.Things, thing =>
		{
			Assert.Equal(kind, thing.Kind);
			Assert.Equal((uint)1, thing.FrameGroups.Single().Width);
			Assert.Equal((uint)1, thing.FrameGroups.Single().Height);
		});
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
