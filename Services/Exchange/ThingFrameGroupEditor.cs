using System;
using System.Collections.Generic;
using NyxAssets.Things;
using NyxAssets.Sprites;

namespace NyxAssetsEditor.Services.Exchange;

/// <summary>Resizes sprite index arrays and frame timings when pattern dimensions change (Object Builder parity).</summary>
public static class ThingFrameGroupEditor
{
	public static void EnsureSpriteCapacity(ThingFrameGroup group)
	{
		var total = group.GetTotalSpriteSlots();
		if (group.SpriteIds.Length == (int)total)
			return;

		var resized = new uint[total];
		var copyLen = Math.Min(group.SpriteIds.Length, (int)total);
		if (copyLen > 0)
			Array.Copy(group.SpriteIds, resized, copyLen);
		group.SpriteIds = resized;
	}

	public static void EnsureFrameTimings(ThingFrameGroup group, uint defaultMinimumMs, uint defaultMaximumMs)
	{
		if (group.Frames <= 1)
		{
			group.IsAnimation = false;
			group.FrameTimings = null;
			return;
		}

		group.IsAnimation = true;
		if (group.FrameTimings != null && group.FrameTimings.Length == (int)group.Frames)
			return;

		var previous = group.FrameTimings;
		group.FrameTimings = new AnimationFrameTiming[group.Frames];
		for (var i = 0; i < group.Frames; i++)
		{
			group.FrameTimings[i] = previous != null && i < previous.Length
				? previous[i]
				: new AnimationFrameTiming(defaultMinimumMs, defaultMaximumMs);
		}
	}

	public static void SetDurationForAllFrames(ThingFrameGroup group, AnimationFrameTiming timing)
	{
		if (group.FrameTimings == null || group.FrameTimings.Length == 0)
			return;

		for (var i = 0; i < group.FrameTimings.Length; i++)
		{
			group.FrameTimings[i] = timing;
		}
	}

	public static int InferCropSize(ThingFrameGroup fg, Func<uint, byte[]?> loadPixels)
	{
		if (fg.Width == 0 || fg.Height == 0)
			return 32;

		var edge = (int)SpritePixelCodec.SpriteEdgeLength;
		var cellW = (int)(fg.Width * edge);
		var cellH = (int)(fg.Height * edge);
		int maxS = 0;

		var spriteCache = new Dictionary<uint, byte[]>();

		for (uint wIndex = 0; wIndex < fg.Width; wIndex++)
		{
			for (uint hIndex = 0; hIndex < fg.Height; hIndex++)
			{
				for (uint lIndex = 0; lIndex < fg.Layers; lIndex++)
				{
					for (uint xIndex = 0; xIndex < fg.PatternX; xIndex++)
					{
						for (uint yIndex = 0; yIndex < fg.PatternY; yIndex++)
						{
							for (uint zIndex = 0; zIndex < fg.PatternZ; zIndex++)
							{
								for (uint fIndex = 0; fIndex < fg.Frames; fIndex++)
								{
									if (fg.TryGetSpriteId(wIndex, hIndex, lIndex, xIndex, yIndex, zIndex, fIndex, out var spriteId) && spriteId != 0)
									{
										if (!spriteCache.TryGetValue(spriteId, out var pixels))
										{
											pixels = loadPixels(spriteId);
											if (pixels != null)
												spriteCache[spriteId] = pixels;
										}

										if (pixels == null) continue;

										var spriteLeft = (int)((fg.Width - wIndex - 1) * edge);
										var spriteTop = (int)((fg.Height - hIndex - 1) * edge);

										for (int sy = 0; sy < edge; sy++)
										{
											for (int sx = 0; sx < edge; sx++)
											{
												var alpha = pixels[(sy * edge + sx) * 4 + 3];
												if (alpha > 0)
												{
													var x = spriteLeft + sx;
													var y = spriteTop + sy;
													var reqS = Math.Max(cellW - x, cellH - y);
													if (reqS > maxS)
													{
														maxS = reqS;
													}
												}
											}
										}
									}
								}
							}
						}
					}
				}
			}
		}

		return maxS > 0 ? Math.Clamp(maxS, 1, 64) : 32;
	}
}
