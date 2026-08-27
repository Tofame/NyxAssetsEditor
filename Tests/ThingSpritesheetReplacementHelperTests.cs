using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NyxAssets.Things;
using NyxAssetsEditor.Services.ImportExport;
using NyxAssetsEditor.Services.Rendering;
using NyxAssetsEditor.Services.Replacement;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;
using NyxAssetsEditor.ViewModels.Pages;
using SkiaSharp;
using Xunit;

namespace NyxAssetsEditor.Tests;

public class ThingSpritesheetReplacementHelperTests
{
	[Fact]
	public void Negative_TryCreateReplacementDocument_WhenFileNotFound_ReturnsError()
	{
		var outfit = CreateSampleOutfit();
		var nonExistentPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");

		var success = ThingSpritesheetReplacementHelper.TryCreateReplacementDocument(outfit, nonExistentPath, out var doc, out var error);

		Assert.False(success);
		Assert.Null(doc);
		Assert.NotNull(error);
		Assert.Contains("Failed to load image", error);
	}

	[Fact]
	public void Negative_TryExtractSpritePixels_WhenFileNotFound_ReturnsError()
	{
		var outfit = CreateSampleOutfit();
		var nonExistentPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");

		var success = ThingSpritesheetReplacementHelper.TryExtractSpritePixels(outfit, nonExistentPath, out var pixels, out var error);

		Assert.False(success);
		Assert.Null(pixels);
		Assert.NotNull(error);
		Assert.Contains("Failed to load image", error);
	}

	[Fact]
	public void Negative_TryCreateReplacementDocument_WhenTargetThingHasNoFrameGroups_ReturnsError()
	{
		var emptyThing = new ThingType { Id = 1, Kind = ThingKind.Outfit };
		var tempPath = CreateTempPng(128, 32, SKColors.White);
		try
		{
			var success = ThingSpritesheetReplacementHelper.TryCreateReplacementDocument(emptyThing, tempPath, out var doc, out var error);

			Assert.False(success);
			Assert.Null(doc);
			Assert.NotNull(error);
			Assert.Contains("no frame groups", error);
		}
		finally
		{
			DeleteTempFile(tempPath);
		}
	}

	[Fact]
	public void Negative_TryExtractSpritePixels_WhenTargetThingHasNoFrameGroups_ReturnsError()
	{
		var emptyThing = new ThingType { Id = 1, Kind = ThingKind.Outfit };
		var tempPath = CreateTempPng(128, 32, SKColors.White);
		try
		{
			var success = ThingSpritesheetReplacementHelper.TryExtractSpritePixels(emptyThing, tempPath, out var pixels, out var error);

			Assert.False(success);
			Assert.Null(pixels);
			Assert.NotNull(error);
			Assert.Contains("no frame groups", error);
		}
		finally
		{
			DeleteTempFile(tempPath);
		}
	}

	[Theory]
	[InlineData(32, 32)]
	[InlineData(64, 32)]
	[InlineData(128, 64)]
	[InlineData(256, 32)]
	public void Negative_TryCreateReplacementDocument_WhenImageDimensionsMismatch_ReturnsDescriptiveError(int width, int height)
	{
		var outfit = CreateSampleOutfit(); // Expected: 128x32
		var tempPath = CreateTempPng(width, height, SKColors.Red);
		try
		{
			var success = ThingSpritesheetReplacementHelper.TryCreateReplacementDocument(outfit, tempPath, out var doc, out var error);

			Assert.False(success);
			Assert.Null(doc);
			Assert.NotNull(error);
			Assert.Contains($"dimensions ({width}x{height}) do not match the expected dimensions (128x32)", error);
		}
		finally
		{
			DeleteTempFile(tempPath);
		}
	}

	[Fact]
	public void Negative_TryExtractSpritePixels_WhenImageDimensionsMismatch_ReturnsDescriptiveError()
	{
		var outfit = CreateSampleOutfit(); // Expected: 128x32
		var tempPath = CreateTempPng(64, 64, SKColors.Red);
		try
		{
			var success = ThingSpritesheetReplacementHelper.TryExtractSpritePixels(outfit, tempPath, out var pixels, out var error);

			Assert.False(success);
			Assert.Null(pixels);
			Assert.NotNull(error);
			Assert.Contains("dimensions (64x64) do not match the expected dimensions (128x32)", error);
		}
		finally
		{
			DeleteTempFile(tempPath);
		}
	}

	[Fact]
	public void Positive_TryCreateReplacementDocument_WhenValidOutfitSheet_CreatesThingDocumentAndExtractsSprites()
	{
		var outfit = CreateSampleOutfit(); // 128x32 (4 directions, 1 frame, 1x1 tile)
		var tempPath = CreateTempPng(128, 32, SKColors.Blue);
		try
		{
			var success = ThingSpritesheetReplacementHelper.TryCreateReplacementDocument(outfit, tempPath, out var doc, out var error);

			Assert.True(success, error);
			Assert.NotNull(doc);
			Assert.Null(error);
			Assert.NotNull(doc.SpritesRgba);
			Assert.Equal(4, doc.SpritesRgba.Count);
			Assert.Equal(4, doc.Thing.FrameGroups[0].SpriteIds.Length);
		}
		finally
		{
			DeleteTempFile(tempPath);
		}
	}

	[Fact]
	public void Positive_TryCreateReplacementDocument_RemovesMagentaChromaKeyToTransparent()
	{
		var outfit = CreateSampleOutfit();
		var tempPath = CreateTempPng(128, 32, new SKColor(255, 0, 255, 255)); // #FF00FF magenta
		try
		{
			var success = ThingSpritesheetReplacementHelper.TryCreateReplacementDocument(outfit, tempPath, out var doc, out var error);

			Assert.True(success, error);
			Assert.NotNull(doc);
			var firstSprite = doc.SpritesRgba![doc.Thing.FrameGroups[0].SpriteIds[0]];
			// Magenta should be converted to transparent (A = 0)
			Assert.Equal(0, firstSprite[3]);
		}
		finally
		{
			DeleteTempFile(tempPath);
		}
	}

	[Fact]
	public void Positive_TryExtractSpritePixels_WhenValidOutfitSheet_MapsToExistingSpriteIds()
	{
		var outfit = CreateSampleOutfit();
		var tempPath = CreateTempPng(128, 32, new SKColor(10, 20, 30, 255));
		try
		{
			var success = ThingSpritesheetReplacementHelper.TryExtractSpritePixels(outfit, tempPath, out var spritePixels, out var error);

			Assert.True(success, error);
			Assert.NotNull(spritePixels);
			Assert.Null(error);
			Assert.Equal(4, spritePixels.Count);
			Assert.Equal(new uint[] { 1, 2, 3, 4 }, spritePixels.Keys.OrderBy(k => k).ToArray());

			// Validate pixel data in extracted sprite 1
			var bytes = spritePixels[1];
			Assert.Equal(10, bytes[0]);
			Assert.Equal(20, bytes[1]);
			Assert.Equal(30, bytes[2]);
			Assert.Equal(255, bytes[3]);
		}
		finally
		{
			DeleteTempFile(tempPath);
		}
	}

	[Fact]
	public void Positive_TryExtractSpritePixels_MultiFrameMultiGroupOutfit_ExtractsAllSlotsCorrectly()
	{
		// Multi-group outfit: Idle (4 directions, 1 frame) + Walk (4 directions, 2 frames)
		var outfit = new ThingType { Id = 1, Kind = ThingKind.Outfit };
		outfit.FrameGroups.Add(new ThingFrameGroup
		{
			GroupTypeId = 0,
			Width = 1,
			Height = 1,
			Layers = 1,
			PatternX = 4,
			PatternY = 1,
			PatternZ = 1,
			Frames = 1,
			SpriteIds = new uint[] { 10, 11, 12, 13 }
		});
		outfit.FrameGroups.Add(new ThingFrameGroup
		{
			GroupTypeId = 1,
			Width = 1,
			Height = 1,
			Layers = 1,
			PatternX = 4,
			PatternY = 1,
			PatternZ = 1,
			Frames = 2,
			SpriteIds = new uint[] { 20, 21, 22, 23, 24, 25, 26, 27 }
		});

		// Dimensions: 4 columns (128px), 3 rows (96px)
		var tempPath = CreateTempPng(128, 96, new SKColor(50, 60, 70, 255));
		try
		{
			var success = ThingSpritesheetReplacementHelper.TryExtractSpritePixels(outfit, tempPath, out var spritePixels, out var error);

			Assert.True(success, error);
			Assert.NotNull(spritePixels);
			Assert.Equal(12, spritePixels.Count);
			Assert.Contains(10u, spritePixels.Keys);
			Assert.Contains(13u, spritePixels.Keys);
			Assert.Contains(20u, spritePixels.Keys);
			Assert.Contains(27u, spritePixels.Keys);
		}
		finally
		{
			DeleteTempFile(tempPath);
		}
	}

	[Fact]
	public async Task Positive_InPlaceSpritesheetReplacement_OverwritesExistingSpriteIdsWithoutAppending()
	{
		var sprites = new FloatingSpriteLoaderViewModel(new SpriteRenderer());
		await sprites.CreateNewArchiveAsync("assets", 1098);
		for (var i = 0; i < 4; i++)
			sprites.Loader.AddNewSprite();
		sprites.NotifyExternalArchiveMutation();

		var things = new FloatingThingsLoaderViewModel();
		await things.CreateNewArchiveAsync("things", 1098, true, true, true);
		things.LinkedSpritePanel = sprites;
		var pair = new LinkedArchivePair(sprites, things);

		var outfit = CreateSampleOutfit();
		outfit.Id = 1;
		pair.ThingsPanel.Catalog!.PutOutfit(outfit);

		var tempPath = CreateTempPng(128, 32, new SKColor(0, 255, 0, 255));
		try
		{
			var extracted = ThingSpritesheetReplacementHelper.TryExtractSpritePixels(outfit, tempPath, out var spritePixels, out var error);
			Assert.True(extracted, error);
			Assert.NotNull(spritePixels);

			var action = pair.SpritePanel.ApplyReplacementPixels(spritePixels, addMissingTargetIds: true);
			Assert.NotNull(action);

			// Count must stay at 4
			Assert.Equal(4u, pair.SpritePanel.Loader.SpriteCount);

			// Pixels overwritten in-place
			var sprite1 = pair.SpritePanel.Loader.LoadSpritePixels(1);
			Assert.Equal(0, sprite1[0]);
			Assert.Equal(255, sprite1[1]);
			Assert.Equal(0, sprite1[2]);
			Assert.Equal(255, sprite1[3]);
		}
		finally
		{
			DeleteTempFile(tempPath);
		}
	}

	[Fact]
	public async Task Positive_AppendedSpritesheetReplacement_AppendsNewSpritesAtEndWhenUnchecked()
	{
		var sprites = new FloatingSpriteLoaderViewModel(new SpriteRenderer());
		await sprites.CreateNewArchiveAsync("assets", 1098);
		for (var i = 0; i < 4; i++)
			sprites.Loader.AddNewSprite();
		sprites.NotifyExternalArchiveMutation();

		var things = new FloatingThingsLoaderViewModel();
		await things.CreateNewArchiveAsync("things", 1098, true, true, true);
		things.LinkedSpritePanel = sprites;
		var pair = new LinkedArchivePair(sprites, things);

		var outfit = CreateSampleOutfit();
		outfit.Id = 1;
		pair.ThingsPanel.Catalog!.PutOutfit(outfit);

		var tempPath = CreateTempPng(128, 32, new SKColor(200, 100, 50, 255));
		try
		{
			var created = ThingSpritesheetReplacementHelper.TryCreateReplacementDocument(outfit, tempPath, out var sheetDoc, out var error);
			Assert.True(created, error);
			Assert.NotNull(sheetDoc);

			var batch = AssetReplacementService.PrepareSingleThing(sheetDoc!, pair, ThingKind.Outfit, 1);
			Assert.True(batch.CanApply, batch.Error);

			var result = AssetReplacementService.Apply(batch);
			Assert.True(result.Succeeded, result.Message);

			// Sprite count should have increased from 4 to 8
			Assert.Equal(8u, pair.SpritePanel.Loader.SpriteCount);

			// Outfit sprite IDs should be remapped to the newly appended IDs (5, 6, 7, 8)
			var updatedOutfit = pair.ThingsPanel.Catalog.TryGetOutfit(1);
			Assert.NotNull(updatedOutfit);
			Assert.Equal(new uint[] { 5, 6, 7, 8 }, updatedOutfit.FrameGroups[0].SpriteIds);
		}
		finally
		{
			DeleteTempFile(tempPath);
		}
	}

	private static ThingType CreateSampleOutfit()
	{
		var outfit = new ThingType { Id = 1, Kind = ThingKind.Outfit };
		outfit.FrameGroups.Add(new ThingFrameGroup
		{
			Width = 1,
			Height = 1,
			Layers = 1,
			PatternX = 4,
			PatternY = 1,
			PatternZ = 1,
			Frames = 1,
			SpriteIds = new uint[] { 1, 2, 3, 4 },
		});
		return outfit;
	}

	private static string CreateTempPng(int width, int height, SKColor color)
	{
		var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
		using var bmp = new SKBitmap(width, height);
		bmp.Erase(color);
		using var img = SKImage.FromBitmap(bmp);
		using var data = img.Encode(SKEncodedImageFormat.Png, 100);
		using var stream = File.Create(tempPath);
		data.SaveTo(stream);
		return tempPath;
	}

	private static void DeleteTempFile(string path)
	{
		if (File.Exists(path))
		{
			try { File.Delete(path); } catch { /* ignore */ }
		}
	}
}
