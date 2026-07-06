using Avalonia.Media;
using SixLabors.ImageSharp.PixelFormats;

namespace NyxAssetsEditor.Services.Rendering;

public static class AppearanceGridColorParser
{
	public static Rgba32 Parse(string hex, Rgba32 fallback)
	{
		if (string.IsNullOrWhiteSpace(hex))
			return fallback;

		var value = hex.Trim();
		if (!value.StartsWith('#'))
			value = "#" + value;

		try
		{
			var color = Color.Parse(value);
			return new Rgba32(color.R, color.G, color.B, color.A);
		}
		catch
		{
			return fallback;
		}
	}
}
