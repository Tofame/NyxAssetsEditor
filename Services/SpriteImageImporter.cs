using System;
using System.IO;
using NyxAssets.Sprites;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NyxAssetsEditor.Services;

public static class SpriteImageImporter
{
	public static byte[] Load32x32Rgba(string filePath)
	{
		using var image = Image.Load<Rgba32>(filePath);
		image.Mutate(ctx => ctx.Resize(SpritePixelCodec.SpriteEdgeLength, SpritePixelCodec.SpriteEdgeLength));

		if (image.Width != SpritePixelCodec.SpriteEdgeLength || image.Height != SpritePixelCodec.SpriteEdgeLength)
			throw new InvalidOperationException("Failed to resize image to 32×32.");

		var rgba = new byte[SpritePixelCodec.RgbaBufferLength];
		var edge = SpritePixelCodec.SpriteEdgeLength;
		for (var y = 0; y < edge; y++)
		{
			for (var x = 0; x < edge; x++)
			{
				var pixel = image[x, y];
				var o = (y * edge + x) * 4;
				rgba[o] = pixel.R;
				rgba[o + 1] = pixel.G;
				rgba[o + 2] = pixel.B;
				rgba[o + 3] = pixel.A;
			}
		}

		return rgba;
	}

	public static bool IsSupportedImage(string path)
	{
		var ext = Path.GetExtension(path).ToLowerInvariant();
		return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" or ".tga";
	}
}
