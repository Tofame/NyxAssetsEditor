using System;
using System.Linq;
using NyxAssets.Sprites;
using NyxAssets.Things;
using NyxAssets.Things.Frames;
using NyxAssets.Utils;
using NyxAssetsEditor.Services.Archive;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NyxAssetsEditor.Services.Rendering;

public sealed class ThingAppearanceOptions
{
	public int FrameGroupIndex { get; init; }
	public int Layer { get; init; }
	public int Frame { get; init; }
	public uint PatternX { get; init; }
	public uint PatternY { get; init; }
	public uint PatternZ { get; init; }
	public bool ShowGrid { get; init; }
	public bool ShowCropSize { get; init; }
}

public static class ThingAppearanceRenderer
{
	public static byte[]? Render(ThingType thing, SpriteLoader loader, ThingAppearanceOptions options)
	{
		if (thing.FrameGroups.Count == 0)
			return null;

		var groupIndex = Math.Clamp(options.FrameGroupIndex, 0, thing.FrameGroups.Count - 1);
		var fg = thing.FrameGroups[groupIndex];
		if (fg.Width == 0 || fg.Height == 0)
			return null;

		var edge = SpritePixelCodec.SpriteEdgeLength;
		var canvasW = (int)(fg.Width * edge);
		var canvasH = (int)(fg.Height * edge);
		using var canvas = new Image<Rgba32>(canvasW, canvasH, default);

		if (!DrawFrameGroupCell(canvas, fg, loader, options, 0, 0))
			return null;

		if (options.ShowGrid)
			DrawGrid(canvas, edge, (int)fg.Width, (int)fg.Height);

		if (options.ShowCropSize && fg.ExactSize > 0 && fg.ExactSize < edge)
			DrawCropRect(canvas, (int)fg.ExactSize, canvasW, canvasH);

		return ExtractRgba(canvas);
	}

	/// <summary>
	/// Renders every pattern slot in a grid (Object Builder <c>updateView</c>):
	/// columns = PatternX × Width, rows = PatternY × Height (each cell is one 32×32 tile).
	/// </summary>
	public static byte[]? RenderPatternGrid(ThingType thing, SpriteLoader loader, ThingAppearanceOptions options)
	{
		if (thing.FrameGroups.Count == 0)
			return null;

		var groupIndex = Math.Clamp(options.FrameGroupIndex, 0, thing.FrameGroups.Count - 1);
		var fg = thing.FrameGroups[groupIndex];
		if (fg.Width == 0 || fg.Height == 0 || fg.PatternX == 0 || fg.PatternY == 0)
			return null;

		var edge = SpritePixelCodec.SpriteEdgeLength;
		var cellW = (int)(fg.Width * edge);
		var cellH = (int)(fg.Height * edge);
		var canvasW = Math.Max(edge, (int)(fg.PatternX * fg.Width * edge));
		var canvasH = Math.Max(edge, (int)(fg.PatternY * fg.Height * edge));
		using var canvas = new Image<Rgba32>(canvasW, canvasH, default);

		var drewAny = false;
		for (uint py = 0; py < fg.PatternY; py++)
		{
			for (uint px = 0; px < fg.PatternX; px++)
			{
				var patternOptions = new ThingAppearanceOptions
				{
					FrameGroupIndex = options.FrameGroupIndex,
					Layer = options.Layer,
					Frame = options.Frame,
					PatternX = px,
					PatternY = py,
					PatternZ = options.PatternZ,
					ShowGrid = false,
					ShowCropSize = false,
				};
				var offsetX = (int)(px * cellW);
				var offsetY = (int)(py * cellH);
				if (DrawFrameGroupCell(canvas, fg, loader, patternOptions, offsetX, offsetY))
					drewAny = true;

				if (options.ShowCropSize && fg.ExactSize > 0 && fg.ExactSize < edge)
					DrawCropRect(canvas, (int)fg.ExactSize, cellW, cellH, offsetX, offsetY);
			}
		}

		if (!drewAny)
			return null;

		if (options.ShowGrid)
		{
			DrawGrid(canvas, edge, (int)(fg.PatternX * fg.Width), (int)(fg.PatternY * fg.Height));
			DrawPatternCellBorders(canvas, cellW, cellH, (int)fg.PatternX, (int)fg.PatternY);
		}

		return ExtractRgba(canvas);
	}

	/// <summary>
	/// Renders all eight missile directions in a 3×3 compass grid (center cell empty).
	/// </summary>
	public static byte[]? RenderMissileDirectionGrid(ThingType thing, SpriteLoader loader, ThingAppearanceOptions options)
	{
		if (thing.Kind != ThingKind.Missile || thing.FrameGroups.Count == 0)
			return null;

		var groupIndex = Math.Clamp(options.FrameGroupIndex, 0, thing.FrameGroups.Count - 1);
		var fg = thing.FrameGroups[groupIndex];
		if (fg.Width == 0 || fg.Height == 0)
			return null;

		var edge = SpritePixelCodec.SpriteEdgeLength;
		var cellW = (int)(fg.Width * edge);
		var cellH = (int)(fg.Height * edge);
		var canvasW = cellW * 3;
		var canvasH = cellH * 3;
		using var canvas = new Image<Rgba32>(canvasW, canvasH, default);

		ReadOnlySpan<(Direction8 Direction, int Column, int Row)> slots =
		[
			(Direction8.NorthWest, 0, 0),
			(Direction8.North, 1, 0),
			(Direction8.NorthEast, 2, 0),
			(Direction8.West, 0, 1),
			(Direction8.East, 2, 1),
			(Direction8.SouthWest, 0, 2),
			(Direction8.South, 1, 2),
			(Direction8.SouthEast, 2, 2),
		];

		var drewAny = false;
		foreach (var (direction, column, row) in slots)
		{
			var (patternX, patternY) = MissileDirectionPatterns.GetPattern(direction);
			var cellOptions = new ThingAppearanceOptions
			{
				FrameGroupIndex = options.FrameGroupIndex,
				Layer = options.Layer,
				Frame = options.Frame,
				PatternX = patternX,
				PatternY = patternY,
				PatternZ = options.PatternZ,
				ShowGrid = false,
				ShowCropSize = false,
			};
			var offsetX = column * cellW;
			var offsetY = row * cellH;
			if (DrawFrameGroupCell(canvas, fg, loader, cellOptions, offsetX, offsetY))
				drewAny = true;

			if (options.ShowCropSize && fg.ExactSize > 0 && fg.ExactSize < edge)
				DrawCropRect(canvas, (int)fg.ExactSize, cellW, cellH, offsetX, offsetY);

			if (options.ShowGrid)
				DrawGrid(canvas, edge, (int)fg.Width, (int)fg.Height, offsetX, offsetY, cellW, cellH);
		}

		if (!drewAny)
			return null;

		DrawMissileCompassCellBorders(canvas, cellW, cellH);
		return ExtractRgba(canvas);
	}

	private static bool DrawFrameGroupCell(
		Image<Rgba32> canvas,
		ThingFrameGroup fg,
		SpriteLoader loader,
		ThingAppearanceOptions options,
		int offsetX,
		int offsetY)
	{
		var edge = SpritePixelCodec.SpriteEdgeLength;
		var drewAny = false;
		var layer = options.Layer;

		for (uint innerW = 0; innerW < fg.Width; innerW++)
		{
			for (uint innerH = 0; innerH < fg.Height; innerH++)
			{
				if (!fg.TryGetSpriteId(innerW, innerH, (uint)layer, options.PatternX, options.PatternY, options.PatternZ, (uint)options.Frame, out var spriteId)
				    || spriteId == 0)
					continue;

				byte[] pixels;
				try
				{
					pixels = loader.LoadSpritePixels(spriteId);
				}
				catch
				{
					continue;
				}

				var innerX = offsetX + (int)((fg.Width - innerW - 1) * edge);
				var innerY = offsetY + (int)((fg.Height - innerH - 1) * edge);
				SpriteImageExporter.BlitSpriteBufferOnto(canvas, innerX, innerY, pixels);
				drewAny = true;
			}
		}

		return drewAny;
	}

	private static void DrawPatternCellBorders(Image<Rgba32> canvas, int cellW, int cellH, int patternX, int patternY)
	{
		var borderColor = new Rgba32(120, 120, 120, 220);
		for (var x = 1; x < patternX; x++)
		{
			var px = x * cellW;
			for (var y = 0; y < canvas.Height; y++)
				canvas[Math.Clamp(px, 0, canvas.Width - 1), y] = borderColor;
		}

		for (var y = 1; y < patternY; y++)
		{
			var py = y * cellH;
			for (var x = 0; x < canvas.Width; x++)
				canvas[x, Math.Clamp(py, 0, canvas.Height - 1)] = borderColor;
		}
	}

	public static (uint PatternX, uint PatternY) ResolvePatterns(ThingType thing, Direction4 direction4, Direction8 direction8)
	{
		if (thing.FrameGroups.Count == 0)
			return (0, 0);

		var fg = thing.FrameGroups[0];
		return thing.Kind switch
		{
			ThingKind.Outfit => ((uint)(int)direction4, 0u),
			ThingKind.Missile => MissileDirectionPatterns.GetPattern(direction8),
			_ => (0, 0),
		};
	}

	private static void DrawGrid(Image<Rgba32> canvas, int edge, int cols, int rows, int offsetX = 0, int offsetY = 0, int? clipW = null, int? clipH = null)
	{
		var gridColor = new Rgba32(80, 80, 80, 180);
		var maxX = offsetX + (clipW ?? canvas.Width);
		var maxY = offsetY + (clipH ?? canvas.Height);

		for (var x = 1; x < cols; x++)
		{
			var px = offsetX + x * edge;
			if (px >= maxX)
				continue;
			for (var y = offsetY; y < maxY && y < canvas.Height; y++)
				canvas[px, y] = gridColor;
		}

		for (var y = 1; y < rows; y++)
		{
			var py = offsetY + y * edge;
			if (py >= maxY)
				continue;
			for (var x = offsetX; x < maxX && x < canvas.Width; x++)
				canvas[x, py] = gridColor;
		}
	}

	private static void DrawMissileCompassCellBorders(Image<Rgba32> canvas, int cellW, int cellH)
	{
		var borderColor = new Rgba32(90, 90, 90, 220);
		for (var row = 0; row < 3; row++)
		{
			for (var col = 0; col < 3; col++)
			{
				if (col == 1 && row == 1)
					continue;

				DrawRectBorder(canvas, col * cellW, row * cellH, cellW, cellH, borderColor);
			}
		}
	}

	private static void DrawRectBorder(Image<Rgba32> canvas, int x, int y, int width, int height, Rgba32 color)
	{
		var right = Math.Min(x + width - 1, canvas.Width - 1);
		var bottom = Math.Min(y + height - 1, canvas.Height - 1);
		x = Math.Clamp(x, 0, canvas.Width - 1);
		y = Math.Clamp(y, 0, canvas.Height - 1);

		for (var px = x; px <= right; px++)
		{
			canvas[px, y] = color;
			canvas[px, bottom] = color;
		}

		for (var py = y; py <= bottom; py++)
		{
			canvas[x, py] = color;
			canvas[right, py] = color;
		}
	}

	private static void DrawGrid(Image<Rgba32> canvas, int edge, int cols, int rows)
	{
		DrawGrid(canvas, edge, cols, rows, 0, 0, canvas.Width, canvas.Height);
	}

	private static void DrawCropRect(Image<Rgba32> canvas, int exactSize, int canvasW, int canvasH, int offsetX = 0, int offsetY = 0)
	{
		var cropColor = new Rgba32(80, 220, 80, 220);
		var left = offsetX + (canvasW - exactSize) / 2;
		var top = offsetY + (canvasH - exactSize) / 2;
		var right = left + exactSize - 1;
		var bottom = top + exactSize - 1;

		for (var x = left; x <= right && x < canvas.Width; x++)
		{
			canvas[x, Math.Clamp(top, 0, canvas.Height - 1)] = cropColor;
			canvas[x, Math.Clamp(bottom, 0, canvas.Height - 1)] = cropColor;
		}

		for (var y = top; y <= bottom && y < canvas.Height; y++)
		{
			canvas[Math.Clamp(left, 0, canvas.Width - 1), y] = cropColor;
			canvas[Math.Clamp(right, 0, canvas.Width - 1), y] = cropColor;
		}
	}

	private static byte[] ExtractRgba(Image<Rgba32> image)
	{
		var rgba = new byte[image.Width * image.Height * 4];
		for (var y = 0; y < image.Height; y++)
		{
			for (var x = 0; x < image.Width; x++)
			{
				var pixel = image[x, y];
				var offset = (y * image.Width + x) * 4;
				rgba[offset] = pixel.R;
				rgba[offset + 1] = pixel.G;
				rgba[offset + 2] = pixel.B;
				rgba[offset + 3] = pixel.A;
			}
		}

		return rgba;
	}
}
