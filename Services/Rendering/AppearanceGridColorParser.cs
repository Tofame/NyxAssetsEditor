using Avalonia.Media;
using SkiaSharp;

namespace NyxAssetsEditor.Services.Rendering;

public static class AppearanceGridColorParser
{
	public static SKColor Parse(string hex, SKColor fallback)
	{
		if (string.IsNullOrWhiteSpace(hex))
			return fallback;

		var value = hex.Trim();
		if (!value.StartsWith('#'))
			value = "#" + value;

		try
		{
			var color = Color.Parse(value);
			return new SKColor(color.R, color.G, color.B, color.A);
		}
		catch
		{
			return fallback;
		}
	}
}
