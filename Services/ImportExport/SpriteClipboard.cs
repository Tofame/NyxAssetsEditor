using System;
using System.Collections.Generic;
using System.Linq;
using NyxAssets.Sprites;

namespace NyxAssetsEditor.Services.ImportExport;

public static class SpriteClipboard
{
	private static readonly List<byte[]> _sprites = new();

	public static bool HasData => _sprites.Count > 0;

	public static void Copy(byte[] rgba) => CopyMany(new[] { rgba });

	public static void CopyMany(IEnumerable<byte[]> sprites)
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
	}

	public static IReadOnlyList<byte[]> GetAll()
	{
		return _sprites
			.Select(pixels =>
			{
				var copy = new byte[pixels.Length];
				pixels.CopyTo(copy, 0);
				return copy;
			})
			.ToList();
	}

	public static byte[]? TryGet() => GetAll().FirstOrDefault();
}
