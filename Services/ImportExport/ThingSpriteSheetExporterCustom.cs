using System;
using System.IO;
using NyxAssets.Sprites;
using NyxAssets.Things;
using SkiaSharp;

namespace NyxAssetsEditor.Services.ImportExport;

public static class ThingSpriteSheetExporterCustom
{
	public static bool TryWriteThingSpriteSheetPng(ISpriteSource archive, ThingType thing, string filePath, bool skipWest = false)
	{
		using var fs = File.Create(filePath);
		return TryWriteThingSpriteSheetPng(archive, thing, fs, skipWest);
	}

	public static bool TryWriteThingSpriteSheetJpeg(ISpriteSource archive, ThingType thing, string filePath, int quality = 90, bool skipWest = false)
	{
		using var fs = File.Create(filePath);
		return TryWriteThingSpriteSheetJpeg(archive, thing, fs, quality, skipWest);
	}

	public static bool TryWriteThingSpriteSheetBmp(ISpriteSource archive, ThingType thing, string filePath, bool skipWest = false)
	{
		using var fs = File.Create(filePath);
		return TryWriteThingSpriteSheetBmp(archive, thing, fs, skipWest);
	}

	public static bool TryWriteThingSpriteSheetPng(ISpriteSource archive, ThingType thing, Stream destination, bool skipWest = false) =>
		TryWriteThingSpriteSheet(archive, thing, SKEncodedImageFormat.Png, 100, destination, skipWest);

	public static bool TryWriteThingSpriteSheetJpeg(ISpriteSource archive, ThingType thing, Stream destination, int quality = 90, bool skipWest = false) =>
		TryWriteThingSpriteSheet(archive, thing, SKEncodedImageFormat.Jpeg, quality, destination, skipWest);

	public static bool TryWriteThingSpriteSheetBmp(ISpriteSource archive, ThingType thing, Stream destination, bool skipWest = false) =>
		TryWriteThingSpriteSheet(archive, thing, SKEncodedImageFormat.Bmp, 100, destination, skipWest);

	private static bool TryWriteThingSpriteSheet(ISpriteSource archive, ThingType thing, SKEncodedImageFormat format, int quality, Stream destination, bool skipWest)
	{
		if (thing.FrameGroups.Count == 0)
			return false;

		if (!TryGetThingSpriteSheetDimensions(thing, skipWest, out var totalX, out var maxTileW, out var maxTileH, out var bitmapW, out var bitmapH))
			return false;

		var info = new SKImageInfo(bitmapW, bitmapH, SKColorType.Rgba8888, SKAlphaType.Unpremul);
		using var bitmap = new SKBitmap(info);
		bitmap.Erase(SKColors.Transparent);

		Span<byte> scratch = stackalloc byte[SpritePixelCodec.RgbaBufferLength];
		var cell = SpritePixelCodec.SpriteEdgeLength;
		var pixelsH = (int)(maxTileH * (uint)cell);
		var yOffsetRows = 0;
		foreach (var group in thing.FrameGroups)
		{
			var yPixels = yOffsetRows * pixelsH;
			CompositeFrameGroupOnto(bitmap, archive, group, totalX, maxTileW, maxTileH, scratch, yPixels, skipWest);
			yOffsetRows += (int)group.GetSpriteSheetTextureRows();
		}

		using var image = SKImage.FromBitmap(bitmap);
		if (image == null) return false;
		using var data = image.Encode(format, quality);
		if (data == null) return false;
		data.SaveTo(destination);
		return true;
	}

	public static bool TryGetThingSpriteSheetDimensions(ThingType thing, bool skipWest, out int totalX, out uint maxTileW, out uint maxTileH, out int bitmapW, out int bitmapH)
	{
		totalX = 0;
		maxTileW = 0;
		maxTileH = 0;
		long totalYRows = 0;
		foreach (var g in thing.FrameGroups)
		{
			var effPatternX = (skipWest && g.PatternX >= 4) ? g.PatternX - 1 : g.PatternX;
			var tx = (int)(g.PatternZ * effPatternX * g.Layers);
			if (totalX < tx)
				totalX = tx;
			if (maxTileW < g.Width)
				maxTileW = g.Width;
			if (maxTileH < g.Height)
				maxTileH = g.Height;
			totalYRows += g.GetSpriteSheetTextureRows();
		}

		if (totalX <= 0)
			totalX = 1;
		maxTileW = Math.Max(1u, maxTileW);
		maxTileH = Math.Max(1u, maxTileH);
		var cell = SpritePixelCodec.SpriteEdgeLength;
		var pixelsW = (long)maxTileW * cell;
		var pixelsH = (long)maxTileH * cell;
		var bw = pixelsW * totalX;
		var bh = pixelsH * totalYRows;
		if (bw > int.MaxValue || bh > int.MaxValue || bw <= 0 || bh <= 0)
		{
			bitmapW = bitmapH = 0;
			return false;
		}

		bitmapW = (int)bw;
		bitmapH = (int)bh;
		return true;
	}

	private static void CompositeFrameGroupOnto(
		SKBitmap sheet,
		ISpriteSource archive,
		ThingFrameGroup group,
		int totalXColumns,
		uint sheetTileWidth,
		uint sheetTileHeight,
		Span<byte> decodeScratch,
		int destYOffsetPixels,
		bool skipWest)
	{
		var cell = SpritePixelCodec.SpriteEdgeLength;
		var pixelsWidth = (int)(sheetTileWidth * (uint)cell);
		var pixelsHeight = (int)(sheetTileHeight * (uint)cell);
		var totalX = Math.Max(1, totalXColumns);
		var effPatternX = (skipWest && group.PatternX >= 4) ? group.PatternX - 1 : group.PatternX;

		for (var f = 0u; f < group.Frames; f++)
		{
			for (var z = 0u; z < group.PatternZ; z++)
			{
				for (var py = 0u; py < group.PatternY; py++)
				{
					for (var px = 0u; px < group.PatternX; px++)
					{
						if (skipWest && group.PatternX >= 4 && px == 3)
							continue;

						var mappedPx = (skipWest && group.PatternX >= 4 && px > 3) ? px - 1 : px;

						for (var l = 0u; l < group.Layers; l++)
						{
							var col = (z * effPatternX + mappedPx) * group.Layers + l;
							var row = f * group.PatternY + py;

							var fx = (int)col * pixelsWidth;
							var fy = (int)row * pixelsHeight + destYOffsetPixels;

							for (var w = 0u; w < group.Width; w++)
							{
								for (var h = 0u; h < group.Height; h++)
								{
									if (!group.TryGetSpriteId(w, h, l, px, py, z, f, out var spriteId) || spriteId == 0)
										continue;
									if (!archive.TryDecodeSpriteById(spriteId, decodeScratch))
										continue;
									var innerX = (int)((group.Width - w - 1) * (uint)cell);
									var innerY = (int)((group.Height - h - 1) * (uint)cell);
									BlitSpriteBufferOnto(sheet, fx + innerX, fy + innerY, decodeScratch);
								}
							}
						}
					}
				}
			}
		}
	}

	private static void BlitSpriteBufferOnto(SKBitmap dest, int destX, int destY, ReadOnlySpan<byte> rgbaBuffer)
	{
		var edge = 32;
		for (var y = 0; y < edge; y++)
		{
			var dstY = destY + y;
			if (dstY < 0 || dstY >= dest.Height) continue;

			for (var x = 0; x < edge; x++)
			{
				var dstX = destX + x;
				if (dstX < 0 || dstX >= dest.Width) continue;

				var srcOffset = (y * edge + x) * 4;
				byte r = rgbaBuffer[srcOffset];
				byte g = rgbaBuffer[srcOffset + 1];
				byte b = rgbaBuffer[srcOffset + 2];
				byte a = rgbaBuffer[srcOffset + 3];

				if (a == 0)
					continue;

				if (a == 255)
				{
					dest.SetPixel(dstX, dstY, new SKColor(r, g, b, a));
				}
				else
				{
					var existing = dest.GetPixel(dstX, dstY);
					if (existing.Alpha == 0)
					{
						dest.SetPixel(dstX, dstY, new SKColor(r, g, b, a));
					}
					else
					{
						float sa = a / 255f;
						float da = existing.Alpha / 255f;
						float outA = sa + da * (1f - sa);
						if (outA > 0)
						{
							byte outR = (byte)Math.Clamp((r * sa + existing.Red * da * (1f - sa)) / outA, 0, 255);
							byte outG = (byte)Math.Clamp((g * sa + existing.Green * da * (1f - sa)) / outA, 0, 255);
							byte outB = (byte)Math.Clamp((b * sa + existing.Blue * da * (1f - sa)) / outA, 0, 255);
							byte outAlpha = (byte)Math.Clamp(outA * 255f, 0, 255);
							dest.SetPixel(dstX, dstY, new SKColor(outR, outG, outB, outAlpha));
						}
					}
				}
			}
		}
	}
}
