using System;
using System.Collections.Generic;
using NyxAssets.Things;
using NyxAssets.Sprites;

namespace NyxAssetsEditor.Services.Exchange;

/// <summary>Resizes sprite index arrays and frame timings when pattern dimensions change (Object Builder parity).</summary>
public static class ThingFrameGroupEditor
{
	/// <summary>Snapshot of sprite layout before a dimension mutation (for coordinate remapping).</summary>
	public readonly struct SpriteLayoutSnapshot
	{
		public uint Width { get; init; }
		public uint Height { get; init; }
		public uint Layers { get; init; }
		public uint PatternX { get; init; }
		public uint PatternY { get; init; }
		public uint PatternZ { get; init; }
		public uint Frames { get; init; }
		public uint[] SpriteIds { get; init; }
	}

	public static SpriteLayoutSnapshot CaptureSpriteLayout(ThingFrameGroup group) => new()
	{
		Width = group.Width,
		Height = group.Height,
		Layers = group.Layers,
		PatternX = group.PatternX,
		PatternY = group.PatternY,
		PatternZ = group.PatternZ,
		Frames = group.Frames,
		SpriteIds = group.SpriteIds ?? Array.Empty<uint>()
	};

	/// <summary>
	/// Allocates <see cref="ThingFrameGroup.SpriteIds"/> to match current dimensions.
	/// Does not preserve slots across dimension changes — use
	/// <see cref="RemapSpriteIdsAfterDimensionChange"/> for that.
	/// </summary>
	public static void EnsureSpriteCapacity(ThingFrameGroup group)
	{
		var total = checked((int)group.GetTotalSpriteSlots());
		if (group.SpriteIds.Length == total)
			return;

		group.SpriteIds = new uint[total];
	}

	/// <summary>
	/// Remaps sprite IDs by (w,h,layer,px,py,pz,frame) so existing slots survive
	/// Frames / Pattern / Width / Height / Layers changes. Overlapping coords keep
	/// their IDs; newly added slots stay 0.
	/// </summary>
	public static void RemapSpriteIdsAfterDimensionChange(ThingFrameGroup group, SpriteLayoutSnapshot previous)
	{
		var total = checked((int)group.GetTotalSpriteSlots());
		var resized = new uint[total];
		var prevIds = previous.SpriteIds;
		if (prevIds == null || prevIds.Length == 0)
		{
			group.SpriteIds = resized;
			return;
		}

		// Same layout size already → keep (or pad/trim via copy if length drifted)
		if (previous.Width == group.Width
			&& previous.Height == group.Height
			&& previous.Layers == group.Layers
			&& previous.PatternX == group.PatternX
			&& previous.PatternY == group.PatternY
			&& previous.PatternZ == group.PatternZ
			&& previous.Frames == group.Frames)
		{
			if (prevIds.Length == total)
			{
				group.SpriteIds = prevIds;
				return;
			}

			var copyLen = Math.Min(prevIds.Length, total);
			if (copyLen > 0)
				Array.Copy(prevIds, resized, copyLen);
			group.SpriteIds = resized;
			return;
		}

		var wMax = Math.Min(group.Width, previous.Width);
		var hMax = Math.Min(group.Height, previous.Height);
		var lMax = Math.Min(group.Layers, previous.Layers);
		var xMax = Math.Min(group.PatternX, previous.PatternX);
		var yMax = Math.Min(group.PatternY, previous.PatternY);
		var zMax = Math.Min(group.PatternZ, previous.PatternZ);
		var fMax = Math.Min(group.Frames, previous.Frames);

		for (uint f = 0; f < fMax; f++)
		for (uint z = 0; z < zMax; z++)
		for (uint y = 0; y < yMax; y++)
		for (uint x = 0; x < xMax; x++)
		for (uint l = 0; l < lMax; l++)
		for (uint h = 0; h < hMax; h++)
		for (uint w = 0; w < wMax; w++)
		{
			var oldIndex = ComputeSpriteIndex(
				w, h, l, x, y, z, f,
				previous.Width, previous.Height, previous.Layers,
				previous.PatternX, previous.PatternY, previous.PatternZ, previous.Frames);
			if (oldIndex >= (uint)prevIds.Length)
				continue;

			var newIndex = group.GetSpriteIndex(w, h, l, x, y, z, f);
			if (newIndex < (uint)resized.Length)
				resized[newIndex] = prevIds[oldIndex];
		}

		group.SpriteIds = resized;
	}

	/// <summary>Same formula as <see cref="ThingFrameGroup.GetSpriteIndex"/> for an arbitrary prior layout.</summary>
	private static uint ComputeSpriteIndex(
		uint innerWidth, uint innerHeight, uint layer,
		uint patternX, uint patternY, uint patternZ, uint frame,
		uint width, uint height, uint layers,
		uint patternXCount, uint patternYCount, uint patternZCount, uint frames)
	{
		var f = frames != 0 ? frame % frames : 0u;
		var i = f * patternZCount + patternZ;
		i = i * patternYCount + patternY;
		i = i * patternXCount + patternX;
		i = i * layers + layer;
		i = i * height + innerHeight;
		i = i * width + innerWidth;
		return i;
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
