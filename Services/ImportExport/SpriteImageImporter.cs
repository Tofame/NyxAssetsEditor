using System;
using System.IO;
using NyxAssets.Sprites;
using SkiaSharp;

namespace NyxAssetsEditor.Services.ImportExport;

public static class SpriteImageImporter
{
	public static byte[] Load32x32Rgba(string filePath)
	{
		using var original = SKBitmap.Decode(filePath);
		if (original == null)
			throw new InvalidOperationException("Failed to decode image.");

		var edge = SpritePixelCodec.SpriteEdgeLength;
		var info = new SKImageInfo(edge, edge, SKColorType.Rgba8888, SKAlphaType.Unpremul);
		using var resized = original.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
		if (resized == null)
			throw new InvalidOperationException("Failed to resize image to 32×32.");

		var rgba = resized.Bytes;
		if (rgba.Length != SpritePixelCodec.RgbaBufferLength)
			throw new InvalidOperationException("Invalid pixel buffer length.");

		return rgba;
	}

	public static bool IsSupportedImage(string path)
	{
		var ext = Path.GetExtension(path).ToLowerInvariant();
		return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" or ".tga";
	}
}
