using System;
using System.Collections.Generic;

namespace NyxAssetsEditor.Services.Looktypes;

public readonly record struct TibiaOutfitColor(byte Id, byte Red, byte Green, byte Blue)
{
	public string Hex => $"#{Red:X2}{Green:X2}{Blue:X2}";
}

public static class TibiaOutfitPalette
{
	public const int Columns = 19;
	public const int Rows = 7;
	public const int ColorCount = Columns * Rows;

	public static IReadOnlyList<TibiaOutfitColor> Create()
	{
		var colors = new TibiaOutfitColor[ColorCount];
		for (var id = 0; id < colors.Length; id++)
			colors[id] = Get(id);
		return colors;
	}

	public static TibiaOutfitColor Get(int colorId)
	{
		colorId = Math.Clamp(colorId, 0, ColorCount - 1);
		float hue;
		float saturation;
		float value;

		if (colorId % Columns == 0)
		{
			hue = 0;
			saturation = 0;
			value = 1f - (float)colorId / Columns / Rows;
		}
		else
		{
			hue = colorId % Columns / 18f;
			(saturation, value) = (colorId / Columns) switch
			{
				0 => (0.25f, 1f),
				1 => (0.25f, 0.75f),
				2 => (0.50f, 0.75f),
				3 => (0.667f, 0.75f),
				4 => (1f, 1f),
				5 => (1f, 0.75f),
				_ => (1f, 0.50f),
			};
		}

		var (r, g, b) = HsvToRgb(hue, saturation, value);
		return new TibiaOutfitColor((byte)colorId, r, g, b);
	}

	private static (byte Red, byte Green, byte Blue) HsvToRgb(float h, float s, float v)
	{
		if (v <= 0) return (0, 0, 0);
		if (s <= 0)
		{
			var grey = (byte)(v * 255);
			return (grey, grey, grey);
		}

		float r;
		float g;
		float b;
		if (h < 1f / 6f)
		{
			r = v; b = v * (1 - s); g = b + (v - b) * 6 * h;
		}
		else if (h < 2f / 6f)
		{
			g = v; b = v * (1 - s); r = g - (v - b) * (6 * h - 1);
		}
		else if (h < 3f / 6f)
		{
			g = v; r = v * (1 - s); b = r + (v - r) * (6 * h - 2);
		}
		else if (h < 4f / 6f)
		{
			b = v; r = v * (1 - s); g = b - (v - r) * (6 * h - 3);
		}
		else if (h < 5f / 6f)
		{
			b = v; g = v * (1 - s); r = g + (v - g) * (6 * h - 4);
		}
		else
		{
			r = v; g = v * (1 - s); b = r - (v - g) * (6 * h - 5);
		}

		return ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
	}
}
