using System;
using System.Linq;
using NyxAssets.Sprites;
using NyxAssetsEditor.Services.Archive;
using NyxAssets.Things;
using NyxAssets.Things.Frames;
using NyxAssets.Utils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

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

		using var canvas = new Image<Rgba32>(canvasW, canvasH, default);
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
			SpriteImageExporter.BlitSpriteBufferOnto(canvas, innerX, innerY, pixels);
			drewAny = true;
		}

		if (!drewAny)
			return null;

		using var preview = canvasW == edge && canvasH == edge
			? canvas.Clone()
			: ResizeToSpriteEdge(canvas);

		return ExtractRgba(preview);
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

	private static Image<Rgba32> ResizeToSpriteEdge(Image<Rgba32> source)
	{
		var edge = SpritePixelCodec.SpriteEdgeLength;
		var resized = source.Clone(ctx => ctx.Resize(edge, edge, KnownResamplers.NearestNeighbor));
		return resized;
	}

	private static byte[] ExtractRgba(Image<Rgba32> image)
	{
		var edge = SpritePixelCodec.SpriteEdgeLength;
		if (image.Width != edge || image.Height != edge)
			throw new InvalidOperationException($"Preview image must be {edge}×{edge}.");

		var rgba = new byte[SpritePixelCodec.RgbaBufferLength];
		for (var y = 0; y < edge; y++)
		{
			for (var x = 0; x < edge; x++)
			{
				var pixel = image[x, y];
				var offset = (y * edge + x) * 4;
				rgba[offset] = pixel.R;
				rgba[offset + 1] = pixel.G;
				rgba[offset + 2] = pixel.B;
				rgba[offset + 3] = pixel.A;
			}
		}

		return rgba;
	}
}
