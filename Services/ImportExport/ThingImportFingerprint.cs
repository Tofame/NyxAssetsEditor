using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using NyxAssets.Things;
using NyxAssetsEditor.Services.Rendering;

namespace NyxAssetsEditor.Services.ImportExport;

/// <summary>
/// Fingerprints a thing from its south-facing (or first) composed preview frame.
/// Empty frames (no draw, or fully transparent) are ignored.
/// </summary>
public static class ThingImportFingerprint
{
	public static HashSet<string> CreateSet() => new(StringComparer.Ordinal);

	public static string? TryCreate(ThingType thing, IReadOnlyDictionary<uint, byte[]>? spritesRgba) =>
		TryCreate(thing, id =>
			spritesRgba != null && spritesRgba.TryGetValue(id, out var pixels) ? pixels : null);

	public static string? TryCreate(ThingType thing, Func<uint, byte[]?> loadPixels)
	{
		ThingPreviewFrame? frame;
		try
		{
			frame = ThingPreviewRenderer.RenderPreview(thing, loadPixels);
		}
		catch
		{
			return null;
		}

		if (frame == null || IsFullyTransparent(frame.Pixels))
			return null;

		var hash = Convert.ToHexString(SHA256.HashData(frame.Pixels));
		return $"{thing.Kind}:{frame.Width}x{frame.Height}:{hash}";
	}

	public static bool IsFullyTransparent(byte[] rgba)
	{
		for (var i = 3; i < rgba.Length; i += 4)
		{
			if (rgba[i] != 0)
				return false;
		}

		return true;
	}
}
