using System;
using System.IO;
using System.Threading.Tasks;
using NyxAssets.Sprites;
using NyxAssets.Things;
using NyxAssetsEditor.Services.Rendering;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;
using NyxAssetsEditor.ViewModels.Pages;
using SkiaSharp;
using Xunit;

namespace NyxAssetsEditor.Tests;

public class ThingEditorAppearanceImageDropTests
{
	[Fact]
	public async Task Negative_CanAcceptDroppedImage_WhenNoLinkedSpritePanel_ReturnsFalse()
	{
		var (thingVm, _, tempFile) = await CreateTestEditor(hasLinkedSprites: false);
		try
		{
			var result = thingVm.CanAcceptDroppedImage(tempFile);
			Assert.False(result);
		}
		finally
		{
			DeleteTempFile(tempFile);
		}
	}

	[Fact]
	public async Task Negative_CanAcceptDroppedImage_WhenFileNonExistentOrInvalid_ReturnsFalse()
	{
		var (thingVm, _, _) = await CreateTestEditor(hasLinkedSprites: true);
		var nonExistentPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");

		var result = thingVm.CanAcceptDroppedImage(nonExistentPath);
		Assert.False(result);
	}

	[Fact]
	public async Task Negative_CanAcceptDroppedImage_WhenDimensionsNotMultipleOfSpriteSize_ReturnsFalse()
	{
		var (thingVm, _, _) = await CreateTestEditor(hasLinkedSprites: true);
		var invalidFile = CreateTempPng(40, 40, SKColors.Blue);
		try
		{
			var result = thingVm.CanAcceptDroppedImage(invalidFile);
			Assert.False(result);
		}
		finally
		{
			DeleteTempFile(invalidFile);
		}
	}

	[Fact]
	public async Task Negative_CanAcceptDroppedImage_WhenDimensionsExceedThingCapacity_ReturnsFalse()
	{
		// 1x1 item: 64x64 exceeds 32x32 capacity
		var (thingVm, _, _) = await CreateTestEditor(hasLinkedSprites: true, width: 1, height: 1, kind: ThingKind.Item);
		var oversizedFile = CreateTempPng(64, 64, SKColors.Green);
		try
		{
			var result = thingVm.CanAcceptDroppedImage(oversizedFile);
			Assert.False(result);
		}
		finally
		{
			DeleteTempFile(oversizedFile);
		}
	}

	[Fact]
	public async Task Positive_CanAcceptDroppedImage_WhenSingleSpriteMatches_ReturnsTrue()
	{
		var (thingVm, _, _) = await CreateTestEditor(hasLinkedSprites: true, width: 1, height: 1, kind: ThingKind.Item);
		var singleSpriteFile = CreateTempPng(32, 32, SKColors.Yellow);
		try
		{
			var result = thingVm.CanAcceptDroppedImage(singleSpriteFile);
			Assert.True(result);
		}
		finally
		{
			DeleteTempFile(singleSpriteFile);
		}
	}

	[Fact]
	public async Task Positive_CanAcceptDroppedImage_WhenMultiTileWithinBounds_ReturnsTrue()
	{
		// 2x2 item: 64x64 exactly fits 2x2
		var (thingVm, _, _) = await CreateTestEditor(hasLinkedSprites: true, width: 2, height: 2, kind: ThingKind.Item);
		var multiTileFile = CreateTempPng(64, 64, SKColors.Cyan);
		try
		{
			var result = thingVm.CanAcceptDroppedImage(multiTileFile);
			Assert.True(result);
		}
		finally
		{
			DeleteTempFile(multiTileFile);
		}
	}

	[Fact]
	public async Task Positive_SingleSpriteDrop_EmptySlot_ShowsModalAndAddsSprite()
	{
		var (thingVm, pair, _) = await CreateTestEditor(hasLinkedSprites: true, width: 1, height: 1, kind: ThingKind.Item);
		var testFile = CreateTempPng(32, 32, new SKColor(12, 34, 56, 255));
		try
		{
			// Initial sprite IDs are 0 (empty)
			var fg = thingVm.CurrentFrameGroup;
			Assert.Equal(0u, fg.SpriteIds[0]);

			thingVm.HandleImageFileDrop(testFile, 10, 10);

			Assert.True(thingVm.ShowDropImageModal);
			Assert.False(thingVm.CanReplaceDroppedImage); // Empty slot cannot replace
			Assert.Contains("Target Slot is empty", thingVm.DropImageModalText);

			// Confirm Add
			var prevCount = pair.SpritePanel.Loader.SpriteCount;
			thingVm.ConfirmAddDroppedImage();

			Assert.False(thingVm.ShowDropImageModal);
			Assert.True(thingVm.IsDirty);
			Assert.True(fg.SpriteIds[0] > 0);
			Assert.Equal(prevCount + 1, pair.SpritePanel.Loader.SpriteCount);

			// Verify pixel data of new sprite
			var newSpriteId = fg.SpriteIds[0];
			var pixels = pair.SpritePanel.Loader.LoadSpritePixels(newSpriteId);
			Assert.NotNull(pixels);
			Assert.Equal(12, pixels[0]);
			Assert.Equal(34, pixels[1]);
			Assert.Equal(56, pixels[2]);
			Assert.Equal(255, pixels[3]);
		}
		finally
		{
			DeleteTempFile(testFile);
		}
	}

	[Fact]
	public async Task Positive_SingleSpriteDrop_FilledSlot_ShowsModalAndReplacesSpritePixels()
	{
		var (thingVm, pair, _) = await CreateTestEditor(hasLinkedSprites: true, width: 1, height: 1, kind: ThingKind.Item);

		// Populate slot 0 with sprite #1 (initially all red)
		var existingId = pair.SpritePanel.Loader.AddNewSprite();
		var redPixels = new byte[SpritePixelCodec.RgbaBufferLength];
		for (int i = 0; i < redPixels.Length; i += 4)
		{
			redPixels[i] = 255;
			redPixels[i + 3] = 255;
		}
		pair.SpritePanel.Loader.SetSpritePixels(existingId, redPixels);
		pair.SpritePanel.NotifyExternalArchiveMutation();

		var fg = thingVm.CurrentFrameGroup;
		fg.SpriteIds[0] = existingId;
		thingVm.RefreshAppearance();
		// RefreshAppearance resets bounds to 0 in headless mode — restore them.
		thingVm.SetAppearanceBoundsForTesting(32, 32);

		var blueFile = CreateTempPng(32, 32, new SKColor(0, 0, 255, 255));
		try
		{
			thingVm.HandleImageFileDrop(blueFile, 10, 10);

			Assert.True(thingVm.ShowDropImageModal);
			Assert.True(thingVm.CanReplaceDroppedImage); // Slot is occupied, Replace is available
			Assert.Contains($"replace sprite #{existingId}", thingVm.DropImageModalText);

			var prevTotalSprites = pair.SpritePanel.Loader.SpriteCount;

			// Confirm Replace
			thingVm.ConfirmReplaceDroppedImage();

			Assert.False(thingVm.ShowDropImageModal);
			Assert.True(thingVm.IsDirty);
			// Total sprites count did NOT increase
			Assert.Equal(prevTotalSprites, pair.SpritePanel.Loader.SpriteCount);
			// Same sprite ID remains assigned
			Assert.Equal(existingId, fg.SpriteIds[0]);

			// Pixels of sprite #existingId were updated to blue
			var updatedPixels = pair.SpritePanel.Loader.LoadSpritePixels(existingId);
			Assert.Equal(0, updatedPixels[0]);
			Assert.Equal(0, updatedPixels[1]);
			Assert.Equal(255, updatedPixels[2]);
			Assert.Equal(255, updatedPixels[3]);
		}
		finally
		{
			DeleteTempFile(blueFile);
		}
	}

	[Fact]
	public async Task Positive_SingleSpriteDrop_Cancel_ClearsPendingStateWithoutMutation()
	{
		var (thingVm, pair, _) = await CreateTestEditor(hasLinkedSprites: true, width: 1, height: 1, kind: ThingKind.Item);
		var testFile = CreateTempPng(32, 32, SKColors.Magenta);
		try
		{
			var prevCount = pair.SpritePanel.Loader.SpriteCount;
			thingVm.HandleImageFileDrop(testFile, 10, 10);
			Assert.True(thingVm.ShowDropImageModal);

			thingVm.CancelDroppedImage();

			Assert.False(thingVm.ShowDropImageModal);
			Assert.False(thingVm.IsDirty);
			Assert.Equal(prevCount, pair.SpritePanel.Loader.SpriteCount);
			Assert.Equal(0u, thingVm.CurrentFrameGroup.SpriteIds[0]);
		}
		finally
		{
			DeleteTempFile(testFile);
		}
	}

	[Fact]
	public async Task Positive_MultiTileDrop_2x2Item_PopulatesAll4Tiles()
	{
		var (thingVm, pair, _) = await CreateTestEditor(hasLinkedSprites: true, width: 2, height: 2, kind: ThingKind.Item);
		var fg = thingVm.CurrentFrameGroup;
		Assert.Equal(4, fg.SpriteIds.Length);

		// 64x64 test image with 4 distinct colored 32x32 tiles
		var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
		using (var bmp = new SKBitmap(64, 64))
		{
			using (var canvas = new SKCanvas(bmp))
			{
				using var paintTL = new SKPaint { Color = new SKColor(10, 0, 0, 255) };
				using var paintTR = new SKPaint { Color = new SKColor(20, 0, 0, 255) };
				using var paintBL = new SKPaint { Color = new SKColor(30, 0, 0, 255) };
				using var paintBR = new SKPaint { Color = new SKColor(40, 0, 0, 255) };

				canvas.DrawRect(0, 0, 32, 32, paintTL);
				canvas.DrawRect(32, 0, 32, 32, paintTR);
				canvas.DrawRect(0, 32, 32, 32, paintBL);
				canvas.DrawRect(32, 32, 32, 32, paintBR);
			}
			using var img = SKImage.FromBitmap(bmp);
			using var data = img.Encode(SKEncodedImageFormat.Png, 100);
			using var fs = File.Create(tempFile);
			data.SaveTo(fs);
		}

		try
		{
			thingVm.HandleImageFileDrop(tempFile, 10, 10);
			Assert.True(thingVm.ShowDropImageModal);
			Assert.Contains("Multi-tile Image: 64×64 px", thingVm.DropImageModalText);

			var prevCount = pair.SpritePanel.Loader.SpriteCount;
			thingVm.ConfirmAddDroppedImage();

			Assert.False(thingVm.ShowDropImageModal);
			Assert.True(thingVm.IsDirty);
			Assert.Equal(prevCount + 4, pair.SpritePanel.Loader.SpriteCount);

			// All 4 slots have newly assigned positive sprite IDs
			for (int i = 0; i < 4; i++)
			{
				Assert.True(fg.SpriteIds[i] > 0);
			}
		}
		finally
		{
			DeleteTempFile(tempFile);
		}
	}

	[Fact]
	public async Task Positive_OutfitDrop_FullSpritesheet_WithAnimations_PopulatesAllFramesAndDirections()
	{
		// Outfit with 4 directions, 3 animation frames = 12 sprite tiles
		var (thingVm, pair, _) = await CreateTestEditor(hasLinkedSprites: true, width: 1, height: 1, kind: ThingKind.Outfit, frames: 3, patternX: 4);
		var fg = thingVm.CurrentFrameGroup;
		Assert.Equal(12, fg.SpriteIds.Length);
		Assert.Equal(3u, fg.Frames);
		Assert.Equal(4u, fg.PatternX);

		// Layout: cols = PatternZ(1) * PatternX(4) * Layers(1) = 4 cols -> 4 * 32 = 128 px
		// rows = Frames(3) * PatternY(1) = 3 rows -> 3 * 32 = 96 px
		int sheetW = 128;
		int sheetH = 96;

		var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
		using (var bmp = new SKBitmap(sheetW, sheetH))
		{
			// Fill each tile with color encoded by its frame and direction: R = frame * 50, G = dir * 50
			for (int col = 0; col < 4; col++)
			{
				for (int row = 0; row < 3; row++)
				{
					var color = new SKColor((byte)(row * 50 + 10), (byte)(col * 50 + 10), 80, 255);
					for (int x = 0; x < 32; x++)
					for (int y = 0; y < 32; y++)
						bmp.SetPixel(col * 32 + x, row * 32 + y, color);
				}
			}
			using var img = SKImage.FromBitmap(bmp);
			using var data = img.Encode(SKEncodedImageFormat.Png, 100);
			using var fs = File.Create(tempFile);
			data.SaveTo(fs);
		}

		try
		{
			// Ensure file passes CanAcceptDroppedImage
			Assert.True(thingVm.CanAcceptDroppedImage(tempFile));

			// Drop full outfit sheet
			thingVm.HandleImageFileDrop(tempFile, 16, 16);

			Assert.True(thingVm.ShowDropImageModal);
			Assert.Contains("Detected Full Outfit Spritesheet", thingVm.DropImageModalText);
			Assert.Contains("3 frame(s) across 4 direction(s)", thingVm.DropImageModalText);

			var prevCount = pair.SpritePanel.Loader.SpriteCount;
			thingVm.ConfirmAddDroppedImage();

			Assert.False(thingVm.ShowDropImageModal);
			Assert.True(thingVm.IsDirty);
			Assert.Equal(prevCount + 12, pair.SpritePanel.Loader.SpriteCount);

			// Verify that every single frame (0, 1, 2) and direction (px 0, 1, 2, 3) has an assigned sprite
			for (uint f = 0; f < 3; f++)
			{
				for (uint px = 0; px < 4; px++)
				{
					var spriteId = fg.GetSpriteId(0, 0, 0, px, 0, 0, f);
					Assert.True(spriteId > 0, $"Expected positive spriteId at frame {f}, px {px}");

					var pixels = pair.SpritePanel.Loader.LoadSpritePixels(spriteId);
					Assert.NotNull(pixels);

					// Check that animation frames have distinct, correct frame colors
					byte expectedR = (byte)(f * 50 + 10);
					byte expectedG = (byte)(px * 50 + 10);
					Assert.Equal(expectedR, pixels[0]);
					Assert.Equal(expectedG, pixels[1]);
				}
			}
		}
		finally
		{
			DeleteTempFile(tempFile);
		}
	}

	[Fact]
	public async Task Positive_OutfitDrop_3DirectionSheet_CopiesEastToWest()
	{
		// 3-direction outfit sheet: cols = 3 * 32 = 96 px, rows = 1 * 32 = 32 px
		var (thingVm, pair, _) = await CreateTestEditor(hasLinkedSprites: true, width: 1, height: 1, kind: ThingKind.Outfit, frames: 1, patternX: 4);
		var fg = thingVm.CurrentFrameGroup;

		int sheetW = 96;
		int sheetH = 32;

		var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
		using (var bmp = new SKBitmap(sheetW, sheetH))
		{
			// col 0 = North (px 0): R=100
			// col 1 = East (px 1): R=150
			// col 2 = South (px 2): R=200
			for (int x = 0; x < 32; x++)
			for (int y = 0; y < 32; y++)
			{
				bmp.SetPixel(x, y, new SKColor(100, 0, 0, 255));
				bmp.SetPixel(32 + x, y, new SKColor(150, 0, 0, 255));
				bmp.SetPixel(64 + x, y, new SKColor(200, 0, 0, 255));
			}
			using var img = SKImage.FromBitmap(bmp);
			using var data = img.Encode(SKEncodedImageFormat.Png, 100);
			using var fs = File.Create(tempFile);
			data.SaveTo(fs);
		}

		try
		{
			Assert.True(thingVm.CanAcceptDroppedImage(tempFile));

			thingVm.HandleImageFileDrop(tempFile, 16, 16);
			Assert.True(thingVm.ShowDropImageModal);

			thingVm.ConfirmAddDroppedImage();

			// West (px 3) should have copied East (px 1) pixels
			var eastId = fg.GetSpriteId(0, 0, 0, 1, 0, 0, 0);
			var westId = fg.GetSpriteId(0, 0, 0, 3, 0, 0, 0);

			Assert.True(eastId > 0);
			Assert.True(westId > 0);

			var eastPixels = pair.SpritePanel.Loader.LoadSpritePixels(eastId);
			var westPixels = pair.SpritePanel.Loader.LoadSpritePixels(westId);

			Assert.Equal(150, eastPixels[0]);
			Assert.Equal(150, westPixels[0]);
		}
		finally
		{
			DeleteTempFile(tempFile);
		}
	}

	private static async Task<(FloatingThingEditorViewModel ThingVm, LinkedArchivePair Pair, string TempFile)> CreateTestEditor(
		bool hasLinkedSprites,
		uint width = 1,
		uint height = 1,
		ThingKind kind = ThingKind.Item,
		uint frames = 1,
		uint patternX = 1,
		uint patternY = 1,
		uint patternZ = 1,
		uint layers = 1)
	{
		var sprites = new FloatingSpriteLoaderViewModel(new SpriteRenderer());
		await sprites.CreateNewArchiveAsync("assets", 1098);

		var things = new FloatingThingsLoaderViewModel();
		await things.CreateNewArchiveAsync("things", 1098, true, true, true);

		if (hasLinkedSprites)
			things.LinkedSpritePanel = sprites;

		var pair = new LinkedArchivePair(sprites, things);

		// Items require Id >= 100; outfits/effects/missiles allow Id = 1
		uint thingId = kind == ThingKind.Item ? 100u : 1u;
		var thing = new ThingType { Id = thingId, Kind = kind };
		uint totalSlots = width * height * layers * patternX * patternY * patternZ * frames;
		thing.FrameGroups.Add(new ThingFrameGroup
		{
			Width = width,
			Height = height,
			Layers = layers,
			PatternX = patternX,
			PatternY = patternY,
			PatternZ = patternZ,
			Frames = frames,
			SpriteIds = new uint[totalSlots],
		});

		if (kind == ThingKind.Outfit)
			things.Catalog!.PutOutfit(thing);
		else if (kind == ThingKind.Effect)
			things.Catalog!.PutEffect(thing);
		else if (kind == ThingKind.Missile)
			things.Catalog!.PutMissile(thing);
		else
			things.Catalog!.PutItem(thing);

		var thingVm = new FloatingThingEditorViewModel(things, thing);

		// Set appearance bounds so CanAcceptDroppedImage and HandleImageFileDrop work
		// in a headless test environment (no UI bounds from the renderer).
		int edge = SpritePixelCodec.SpriteEdgeLength;
		thingVm.SetAppearanceBoundsForTesting((int)(width * edge), (int)(height * edge));

		var tempFile = CreateTempPng(32, 32, SKColors.White);

		return (thingVm, pair, tempFile);
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
