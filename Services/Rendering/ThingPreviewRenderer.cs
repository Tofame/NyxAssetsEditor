using System;
using System.Linq;
using NyxAssets.Sprites;
using NyxAssetsEditor.Services.Archive;
using NyxAssets.Things;
using NyxAssets.Things.Frames;
using SkiaSharp;
using System.Runtime.InteropServices;

namespace NyxAssetsEditor.Services.Rendering;

public static class ThingPreviewRenderer
{
	private static readonly OutfitFrameRequest SouthIdleOutfit = new()
	{
		Direction = (int)Direction4.South,
		WalkPhase = 0,
		AddonMask = 0,
	};

	private static readonly MissileFrameRequest SouthMissile = new()
	{
		Direction = Direction8.South,
	};

	public static byte[]? RenderPreviewRgba(ThingType thing, SpriteLoader loader)
	{
		if (thing.FrameGroups.Count == 0)
			return null;

		if (!TryResolveSelection(thing, out var selection))
			return null;

		var fg = selection.FrameGroup;
		var edge = SpritePixelCodec.SpriteEdgeLength;
		var canvasW = (int)(fg.Width * edge);
		var canvasH = (int)(fg.Height * edge);
		if (canvasW <= 0 || canvasH <= 0)
			return null;

		var canvas = new byte[canvasW * canvasH * 4];
		var drewAny = false;

		var baseLayerOnly = thing.Kind == ThingKind.Outfit;

		foreach (var slot in selection.EnumerateSpriteSlots().OrderBy(s => s.Layer))
		{
			if (baseLayerOnly && slot.Layer != 0)
				continue;

			if (slot.SpriteId == 0)
				continue;

			byte[] pixels;
			try
			{
				pixels = loader.LoadSpritePixels(slot.SpriteId);
			}
			catch
			{
				continue;
			}

			// Match ThingSpriteSheetExporter inner-tile placement (Asset Editor origin).
			var innerX = (int)((fg.Width - slot.InnerWidth - 1) * edge);
			var innerY = (int)((fg.Height - slot.InnerHeight - 1) * edge);
			BlitSpriteBuffer(canvas, canvasW, canvasH, innerX, innerY, pixels);
			drewAny = true;
		}

		if (!drewAny)
			return null;

		if (canvasW == edge && canvasH == edge)
		{
			return canvas;
		}

		return ResizeToSpriteEdge(canvas, canvasW, canvasH);
	}

	private static bool TryResolveSelection(ThingType thing, out ThingFrameSelection selection)
	{
		try
		{
			switch (thing.Kind)
			{
				case ThingKind.Item:
					selection = ThingFrameResolver.GetItemFrame(thing, new ItemFrameRequest { Frame = 0 });
					return true;
				case ThingKind.Outfit:
					selection = ThingFrameResolver.GetOutfitFrame(thing, SouthIdleOutfit);
					return true;
				case ThingKind.Effect:
					selection = ThingFrameResolver.GetEffectFrame(thing, new EffectFrameRequest { Frame = 0 });
					return true;
				case ThingKind.Missile:
					selection = ThingFrameResolver.GetMissileFrame(thing, SouthMissile);
					return true;
				default:
					selection = default;
					return false;
			}
		}
		catch
		{
			selection = default;
			return false;
		}
	}

	private static byte[] ResizeToSpriteEdge(byte[] source, int srcW, int srcH)
	{
		var edge = SpritePixelCodec.SpriteEdgeLength;
		var srcInfo = new SKImageInfo(srcW, srcH, SKColorType.Rgba8888, SKAlphaType.Unpremul);
		using var original = new SKBitmap();
		var pin = GCHandle.Alloc(source, GCHandleType.Pinned);
		try
		{
			original.InstallPixels(srcInfo, pin.AddrOfPinnedObject(), srcInfo.RowBytes);
			var dstInfo = new SKImageInfo(edge, edge, SKColorType.Rgba8888, SKAlphaType.Unpremul);
			using var resized = original.Resize(dstInfo, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
			return resized.Bytes;
		}
		finally
		{
			pin.Free();
		}
	}

	private static void BlitSpriteBuffer(byte[] dst, int dstW, int dstH, int x, int y, byte[] src)
	{
		var edge = SpritePixelCodec.SpriteEdgeLength;
		for (var sy = 0; sy < edge; sy++)
		{
			var dy = y + sy;
			if (dy < 0 || dy >= dstH) continue;
			for (var sx = 0; sx < edge; sx++)
			{
				var dx = x + sx;
				if (dx < 0 || dx >= dstW) continue;

				var srcOffset = (sy * edge + sx) * 4;
				var dstOffset = (dy * dstW + dx) * 4;

				var srcA = src[srcOffset + 3];
				if (srcA == 0)
					continue;

				if (srcA == 255)
				{
					dst[dstOffset] = src[srcOffset];
					dst[dstOffset + 1] = src[srcOffset + 1];
					dst[dstOffset + 2] = src[srcOffset + 2];
					dst[dstOffset + 3] = src[srcOffset + 3];
				}
				else
				{
					var sA = srcA / 255f;
					var dA = dst[dstOffset + 3] / 255f;
					var outA = sA + dA * (1 - sA);
					if (outA > 0)
					{
						dst[dstOffset] = (byte)Math.Clamp((src[srcOffset] * sA + dst[dstOffset] * dA * (1 - sA)) / outA, 0, 255);
						dst[dstOffset + 1] = (byte)Math.Clamp((src[srcOffset + 1] * sA + dst[dstOffset + 1] * dA * (1 - sA)) / outA, 0, 255);
						dst[dstOffset + 2] = (byte)Math.Clamp((src[srcOffset + 2] * sA + dst[dstOffset + 2] * dA * (1 - sA)) / outA, 0, 255);
						dst[dstOffset + 3] = (byte)(outA * 255);
					}
				}
			}
		}
	}
}
