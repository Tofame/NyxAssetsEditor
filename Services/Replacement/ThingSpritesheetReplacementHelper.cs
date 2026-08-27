using System;
using System.Collections.Generic;
using System.IO;
using NyxAssets.Sprites;
using NyxAssets.Things;
using NyxAssets.Things.Exchange;
using NyxAssetsEditor.Services.Exchange;
using NyxAssetsEditor.Services.ImportExport;
using SkiaSharp;

namespace NyxAssetsEditor.Services.Replacement;

public static class ThingSpritesheetReplacementHelper
{
	public static bool TryExtractSpritePixels(
		ThingType targetThing,
		string filePath,
		out Dictionary<uint, byte[]>? spritePixels,
		out string? error)
	{
		spritePixels = null;
		error = null;

		if (targetThing.FrameGroups.Count == 0)
		{
			error = "The target thing has no frame groups to replace.";
			return false;
		}

		if (!ThingSpriteSheetExporterCustom.TryGetThingSpriteSheetDimensions(targetThing, skipWest: false, out var totalX, out var maxTileW, out var maxTileH, out var expectedW, out var expectedH))
		{
			error = "Unable to compute the expected spritesheet dimensions for the target.";
			return false;
		}

		SlicerImage image;
		try
		{
			image = SpritesheetSlicerService.Load(filePath);
		}
		catch (Exception ex)
		{
			error = $"Failed to load image: {ex.Message}";
			return false;
		}

		if (image.Width != expectedW || image.Height != expectedH)
		{
			error = $"Spritesheet dimensions ({image.Width}x{image.Height}) do not match the expected dimensions ({expectedW}x{expectedH}) for this {targetThing.Kind.ToString().ToLowerInvariant()}.";
			return false;
		}

		var cell = SpritePixelCodec.SpriteEdgeLength; // 32
		var pixelsWidth = (int)(maxTileW * (uint)cell);
		var pixelsHeight = (int)(maxTileH * (uint)cell);

		var result = new Dictionary<uint, byte[]>();
		var destYOffsetPixels = 0;
		for (var groupIndex = 0; groupIndex < targetThing.FrameGroups.Count; groupIndex++)
		{
			var group = targetThing.FrameGroups[groupIndex];

			for (var f = 0u; f < group.Frames; f++)
			{
				for (var z = 0u; z < group.PatternZ; z++)
				{
					for (var py = 0u; py < group.PatternY; py++)
					{
						for (var px = 0u; px < group.PatternX; px++)
						{
							for (var l = 0u; l < group.Layers; l++)
							{
								var col = (z * group.PatternX + px) * group.Layers + l;
								var row = f * group.PatternY + py;

								var fx = (int)col * pixelsWidth;
								var fy = (int)row * pixelsHeight + destYOffsetPixels;

								for (var w = 0u; w < group.Width; w++)
								{
									for (var h = 0u; h < group.Height; h++)
									{
										if (!group.TryGetSpriteId(w, h, l, px, py, z, f, out var spriteId) || spriteId == 0)
											continue;

										var innerX = (int)((group.Width - w - 1) * (uint)cell);
										var innerY = (int)((group.Height - h - 1) * (uint)cell);

										var spriteX = fx + innerX;
										var spriteY = fy + innerY;

										var spriteBuffer = new byte[SpritePixelCodec.RgbaBufferLength];
										ExtractSpriteCellRgba(image, spriteX, spriteY, spriteBuffer);

										result[spriteId] = spriteBuffer;
									}
								}
							}
						}
					}
				}
			}

			destYOffsetPixels += (int)group.GetSpriteSheetTextureRows() * pixelsHeight;
		}

		spritePixels = result;
		return true;
	}

	public static bool TryCreateReplacementDocument(
		ThingType targetThing,
		string filePath,
		out ThingDocument? document,
		out string? error)
	{
		document = null;
		error = null;

		if (targetThing.FrameGroups.Count == 0)
		{
			error = "The target thing has no frame groups to replace.";
			return false;
		}

		if (!ThingSpriteSheetExporterCustom.TryGetThingSpriteSheetDimensions(targetThing, skipWest: false, out var totalX, out var maxTileW, out var maxTileH, out var expectedW, out var expectedH))
		{
			error = "Unable to compute the expected spritesheet dimensions for the target.";
			return false;
		}

		SlicerImage image;
		try
		{
			image = SpritesheetSlicerService.Load(filePath);
		}
		catch (Exception ex)
		{
			error = $"Failed to load image: {ex.Message}";
			return false;
		}

		if (image.Width != expectedW || image.Height != expectedH)
		{
			error = $"Spritesheet dimensions ({image.Width}x{image.Height}) do not match the expected dimensions ({expectedW}x{expectedH}) for this {targetThing.Kind.ToString().ToLowerInvariant()}.";
			return false;
		}

		var cell = SpritePixelCodec.SpriteEdgeLength; // 32
		var pixelsWidth = (int)(maxTileW * (uint)cell);
		var pixelsHeight = (int)(maxTileH * (uint)cell);

		var replacementThing = ThingCloner.Clone(targetThing, targetThing.Id);
		var spritesRgba = new Dictionary<uint, byte[]>();
		uint nextVirtualSpriteId = 1;

		var destYOffsetPixels = 0;
		for (var groupIndex = 0; groupIndex < replacementThing.FrameGroups.Count; groupIndex++)
		{
			var group = replacementThing.FrameGroups[groupIndex];

			for (var f = 0u; f < group.Frames; f++)
			{
				for (var z = 0u; z < group.PatternZ; z++)
				{
					for (var py = 0u; py < group.PatternY; py++)
					{
						for (var px = 0u; px < group.PatternX; px++)
						{
							for (var l = 0u; l < group.Layers; l++)
							{
								var col = (z * group.PatternX + px) * group.Layers + l;
								var row = f * group.PatternY + py;

								var fx = (int)col * pixelsWidth;
								var fy = (int)row * pixelsHeight + destYOffsetPixels;

								for (var w = 0u; w < group.Width; w++)
								{
									for (var h = 0u; h < group.Height; h++)
									{
										var innerX = (int)((group.Width - w - 1) * (uint)cell);
										var innerY = (int)((group.Height - h - 1) * (uint)cell);

										var spriteX = fx + innerX;
										var spriteY = fy + innerY;

										var spriteBuffer = new byte[SpritePixelCodec.RgbaBufferLength];
										ExtractSpriteCellRgba(image, spriteX, spriteY, spriteBuffer);

										var spriteId = nextVirtualSpriteId++;
										spritesRgba[spriteId] = spriteBuffer;

										var spriteIndex = group.GetSpriteIndex(w, h, l, px, py, z, f);
										group.SpriteIds[spriteIndex] = spriteId;
									}
								}
							}
						}
					}
				}
			}

			destYOffsetPixels += (int)group.GetSpriteSheetTextureRows() * pixelsHeight;
		}

		document = new ThingDocument
		{
			Thing = replacementThing,
			SpritesRgba = spritesRgba
		};
		return true;
	}

	private static void ExtractSpriteCellRgba(SlicerImage image, int startX, int startY, byte[] destination)
	{
		var cell = SpritePixelCodec.SpriteEdgeLength;
		for (var y = 0; y < cell; y++)
		{
			var srcY = startY + y;
			for (var x = 0; x < cell; x++)
			{
				var srcX = startX + x;
				var dstOffset = (y * cell + x) * 4;

				if (srcX >= 0 && srcX < image.Width && srcY >= 0 && srcY < image.Height)
				{
					var srcOffset = (srcY * image.Width + srcX) * 4;
					destination[dstOffset] = image.Rgba[srcOffset];
					destination[dstOffset + 1] = image.Rgba[srcOffset + 1];
					destination[dstOffset + 2] = image.Rgba[srcOffset + 2];
					destination[dstOffset + 3] = image.Rgba[srcOffset + 3];
				}
				else
				{
					destination[dstOffset] = 0;
					destination[dstOffset + 1] = 0;
					destination[dstOffset + 2] = 0;
					destination[dstOffset + 3] = 0;
				}
			}
		}
	}
}
