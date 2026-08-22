using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NyxAssets.Sprites;
using SkiaSharp;

namespace NyxAssetsEditor.Services.Replacement;

public static class SpriteReplacementBackup
{
	public static string BackupDirectory => Path.Combine(AppContext.BaseDirectory, "replacer_backup");

	public static string Write(IReadOnlyDictionary<uint, byte[]> discardedTargetPixels, DateTime timestamp)
	{
		Directory.CreateDirectory(BackupDirectory);
		var stamp = timestamp.ToString("yyyyMMdd_HHmmss");
		foreach (var pair in discardedTargetPixels.OrderBy(entry => entry.Key))
			WritePng(pair.Value, Path.Combine(BackupDirectory, $"{stamp}_{pair.Key}.png"));
		return BackupDirectory;
	}

	private static void WritePng(byte[] pixels, string outputPath)
	{
		var edge = SpritePixelCodec.SpriteEdgeLength;
		var info = new SKImageInfo(edge, edge, SKColorType.Rgba8888, SKAlphaType.Unpremul);
		using var bitmap = new SKBitmap();
		var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
		try
		{
			bitmap.InstallPixels(info, pin.AddrOfPinnedObject(), info.RowBytes);
			using var image = SKImage.FromBitmap(bitmap)
				?? throw new InvalidOperationException("Could not encode a discarded sprite.");
			using var data = image.Encode(SKEncodedImageFormat.Png, 100)
				?? throw new InvalidOperationException("Could not encode a discarded sprite as PNG.");
			using var stream = File.Create(outputPath);
			data.SaveTo(stream);
		}
		finally
		{
			pin.Free();
		}
	}
}
