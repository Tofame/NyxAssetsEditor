using System;
using System.Collections.Generic;
using System.IO;
using NyxAssets.Things;
using NyxAssetsEditor.Services.Archive;
using NyxAssetsEditor.Services.Rendering;
using NyxAssetsEditor.ViewModels.Pages;

namespace NyxAssetsEditor.Services.ImportExport;

/// <summary>
/// Exports an animated <see cref="ThingType"/> as an animated GIF89a file.
/// Frame pixels are produced by <see cref="ThingPreviewRenderer"/> and encoded
/// with <see cref="GifEncoder"/>.
/// </summary>
public static class ThingGifExporter
{
	/// <summary>
	/// Renders all animation frames of <paramref name="thing"/> and writes an
	/// animated .gif to <paramref name="outputPath"/>.
	/// </summary>
	/// <returns><c>true</c> on success, <c>false</c> if the thing has no renderable frames.</returns>
	public static bool TryWriteThingGif(SpriteLoader loader, ThingType thing, string outputPath)
	{
		var frames = BuildFrames(loader, thing);
		if (frames == null || frames.Length == 0)
			return false;

		using var file = File.Create(outputPath);
		GifEncoder.Encode(file, frames);
		return true;
	}

	// ── internals ─────────────────────────────────────────────────────────────

	private static GifEncoder.GifFrame[]? BuildFrames(SpriteLoader loader, ThingType thing)
	{
		if (thing.Kind == ThingKind.Outfit)
			return BuildOutfitFrames(loader, thing);

		int frameCount = GetFrameCount(thing);
		if (frameCount <= 0)
			return null;

		int delayMs = (int)SettingsViewModel.GetDefaultAnimationDurationMs(thing.Kind);
		int delayCs = Math.Max(1, (int)Math.Round(delayMs / 10.0));

		var gifFrames = new List<GifEncoder.GifFrame>(frameCount);

		for (int fi = 0; fi < frameCount; fi++)
		{
			var preview = ThingPreviewRenderer.RenderPreview(thing, loader, fi);
			if (preview == null)
				continue;

			gifFrames.Add(new GifEncoder.GifFrame(preview.Pixels, preview.Width, preview.Height, delayCs));
		}

		return gifFrames.Count > 0 ? gifFrames.ToArray() : null;
	}

	// Direction4: North=0, East=1, South=2, West=3
	private static readonly int[] OutfitDirectionOrder = [2, 3, 0, 1]; // South, West, North, East

	private static GifEncoder.GifFrame[]? BuildOutfitFrames(SpriteLoader loader, ThingType thing)
	{
		if (thing.FrameGroups.Count == 0)
			return null;

		// Walk frames are in frame group 1 if it exists, otherwise frame group 0.
		var walkGroup = thing.FrameGroups.Count > 1 ? thing.FrameGroups[1] : thing.FrameGroups[0];
		int walkFrames = Math.Max(1, (int)walkGroup.Frames);

		int delayMs = (int)SettingsViewModel.GetDefaultAnimationDurationMs(ThingKind.Outfit);
		int delayCs = Math.Max(1, (int)Math.Round(delayMs / 10.0));

		var gifFrames = new List<GifEncoder.GifFrame>(walkFrames * 4);

		foreach (int dir in OutfitDirectionOrder)
		{
			for (int wp = 0; wp < walkFrames; wp++)
			{
				var preview = ThingPreviewRenderer.RenderOutfitPreview(thing, loader, wp, dir);
				if (preview == null)
					continue;

				gifFrames.Add(new GifEncoder.GifFrame(preview.Pixels, preview.Width, preview.Height, delayCs));
			}
		}

		return gifFrames.Count > 0 ? gifFrames.ToArray() : null;
	}

	private static int GetFrameCount(ThingType thing)
	{
		if (thing.FrameGroups.Count == 0)
			return 0;

		switch (thing.Kind)
		{
			case ThingKind.Item:
			case ThingKind.Effect:
			{
				var fg = thing.FrameGroups[0];
				return (int)fg.Frames;
			}
			case ThingKind.Missile:
				return 1;
			default:
				return 0;
		}
	}
}
