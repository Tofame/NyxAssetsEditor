using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using NyxAssets.Sprites;
using NyxAssetsEditor.Models;

namespace NyxAssetsEditor.Services.ImportExport;

public static class SpriteClipboard
{
	private static readonly List<byte[]> _sprites = new();
	private static Avalonia.Media.Imaging.Bitmap? _lastSetSystemBitmap;

	private static IClipboard? GetSystemClipboard()
	{
		if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			return desktop.MainWindow?.Clipboard;
		}
		return null;
	}

	public static bool HasData => _sprites.Count > 0;

	public static async Task CopyAsync(byte[] rgba) => await CopyManyAsync(new[] { rgba });

	public static async Task CopyManyAsync(IEnumerable<byte[]> sprites)
	{
		_sprites.Clear();
		foreach (var rgba in sprites)
		{
			if (rgba.Length != SpritePixelCodec.RgbaBufferLength)
				throw new ArgumentException($"Expected {SpritePixelCodec.RgbaBufferLength} RGBA bytes.");

			var copy = new byte[rgba.Length];
			rgba.CopyTo(copy, 0);
			_sprites.Add(copy);
		}

		if (_sprites.Count > 0)
		{
			var clipboard = GetSystemClipboard();
			if (clipboard != null)
			{
				try
				{
					var rgba = _sprites[0];
					var edge = SpriteModel.SpriteSize;
					var info = new SkiaSharp.SKImageInfo(edge, edge, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul);
					using var skBitmap = new SkiaSharp.SKBitmap(info);
					System.Runtime.InteropServices.Marshal.Copy(rgba, 0, skBitmap.GetPixels(), rgba.Length);
					using var skImage = SkiaSharp.SKImage.FromBitmap(skBitmap);
					using var pngData = skImage.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
					using var ms = new MemoryStream(pngData.ToArray());
					var bitmap = new Avalonia.Media.Imaging.Bitmap(ms);
					_lastSetSystemBitmap = bitmap;
					await clipboard.SetBitmapAsync(bitmap);
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"Failed to set system clipboard bitmap: {ex.Message}");
				}
			}
		}
	}

	private static async Task<byte[]?> DecodeBitmapAsync(Avalonia.Media.Imaging.Bitmap bitmap)
	{
		try
		{
			using var ms = new MemoryStream();
			bitmap.Save(ms);
			ms.Position = 0;

			using var skBitmap = SkiaSharp.SKBitmap.Decode(ms);
			if (skBitmap == null) return null;

			if (skBitmap.Width != SpriteModel.SpriteSize || skBitmap.Height != SpriteModel.SpriteSize)
			{
				return null;
			}

			if (skBitmap.ColorType != SkiaSharp.SKColorType.Rgba8888 || skBitmap.AlphaType != SkiaSharp.SKAlphaType.Unpremul)
			{
				var edge = SpriteModel.SpriteSize;
				var info = new SkiaSharp.SKImageInfo(edge, edge, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul);
				using var target = new SkiaSharp.SKBitmap(info);
				using var canvas = new SkiaSharp.SKCanvas(target);
				canvas.DrawBitmap(skBitmap, 0, 0);
				return target.Bytes;
			}

			return skBitmap.Bytes;
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Failed to decode bitmap: {ex.Message}");
			return null;
		}
	}

	public static async Task<byte[]?> TryGetFromSystemClipboardAsync()
	{
		var clipboard = GetSystemClipboard();
		if (clipboard == null) return null;

		try
		{
			var bitmap = await clipboard.TryGetBitmapAsync();
			if (bitmap == null) return null;
			return await DecodeBitmapAsync(bitmap);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Failed to get system clipboard bitmap: {ex.Message}");
			return null;
		}
	}

	public static async Task<IReadOnlyList<byte[]>> GetAllAsync()
	{
		var clipboard = GetSystemClipboard();
		if (clipboard != null)
		{
			try
			{
				var currentBitmap = await clipboard.TryGetBitmapAsync();
				if (currentBitmap != null)
				{
					if (currentBitmap != _lastSetSystemBitmap)
					{
						var systemRgba = await DecodeBitmapAsync(currentBitmap);
						if (systemRgba != null)
						{
							return new[] { systemRgba };
						}
						return Array.Empty<byte[]>();
					}
				}
				else
				{
					if (_lastSetSystemBitmap != null)
					{
						return Array.Empty<byte[]>();
					}
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Error checking clipboard: {ex.Message}");
			}
		}

		return _sprites
			.Select(pixels =>
			{
				var copy = new byte[pixels.Length];
				pixels.CopyTo(copy, 0);
				return copy;
			})
			.ToList();
	}

	public static async Task<byte[]?> TryGetAsync()
	{
		var all = await GetAllAsync();
		return all.FirstOrDefault();
	}
}
