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
	public bool ShowDragGrid { get; init; }
	public bool ShowCropSize { get; init; }
	public (int X, int Y, int Width, int Height)? HighlightRect { get; init; }
	public Rgba32 GridColor { get; init; } = new(80, 80, 80, 180);
	public int GridLineWidth { get; init; } = 1;
	public Rgba32 DragGridColor { get; init; } = new(255, 105, 180, 180);
	public int DragGridLineWidth { get; init; } = 1;
	public Rgba32 HighlightColor { get; init; } = new(58, 123, 213, 128);
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

		ApplyGridAndHighlight(canvas, options, edge, (int)fg.Width, (int)fg.Height);

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

		ApplyGridAndHighlight(canvas, options, edge, (int)(fg.PatternX * fg.Width), (int)(fg.PatternY * fg.Height));
		if (UsesGrid(options))
			DrawPatternCellBorders(canvas, cellW, cellH, (int)fg.PatternX, (int)fg.PatternY, GetActiveGridStyle(options));

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
		}

		if (!drewAny)
			return null;

		var (borderColor, borderWidth) = UsesGrid(options)
			? GetActiveGridStyle(options)
			: (new Rgba32(90, 90, 90, 220), 1);
		DrawMissileCompassCellBorders(canvas, cellW, cellH, borderColor, borderWidth);

		if (UsesGrid(options))
		{
			var (gridColor, gridLineWidth) = GetActiveGridStyle(options);
			foreach (var (_, column, row) in slots)
			{
				var offsetX = column * cellW;
				var offsetY = row * cellH;
				DrawGrid(canvas, edge, (int)fg.Width, (int)fg.Height, offsetX, offsetY, cellW, cellH, gridColor, gridLineWidth);
			}
		}

		DrawHighlight(canvas, options);

		return ExtractRgba(canvas);
	}

	public static byte[]? RenderDragPreviewOverlay(
		int canvasW,
		int canvasH,
		ThingFrameGroup fg,
		ThingAppearanceOptions options,
		bool isMissile,
		bool showPatternGrid)
	{
		if (canvasW <= 0 || canvasH <= 0 || fg.Width == 0 || fg.Height == 0)
			return null;

		using var canvas = new Image<Rgba32>(canvasW, canvasH, default);
		var edge = SpritePixelCodec.SpriteEdgeLength;

		if (isMissile)
		{
			var cellW = (int)(fg.Width * edge);
			var cellH = (int)(fg.Height * edge);
			var (borderColor, borderWidth) = GetActiveGridStyle(options);
			DrawMissileCompassCellBorders(canvas, cellW, cellH, borderColor, borderWidth);

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

			foreach (var (_, column, row) in slots)
			{
				var offsetX = column * cellW;
				var offsetY = row * cellH;
				DrawGrid(canvas, edge, (int)fg.Width, (int)fg.Height, offsetX, offsetY, cellW, cellH, borderColor, borderWidth);
			}
		}
		else if (showPatternGrid)
		{
			var cellW = (int)(fg.Width * edge);
			var cellH = (int)(fg.Height * edge);
			ApplyGridAndHighlight(canvas, options, edge, (int)(fg.PatternX * fg.Width), (int)(fg.PatternY * fg.Height));
			DrawPatternCellBorders(canvas, cellW, cellH, (int)fg.PatternX, (int)fg.PatternY, GetActiveGridStyle(options));
		}
		else
		{
			ApplyGridAndHighlight(canvas, options, edge, (int)fg.Width, (int)fg.Height);
		}

		DrawHighlight(canvas, options);
		return ExtractRgba(canvas);
	}

	private static bool UsesGrid(ThingAppearanceOptions options) => options.ShowGrid || options.ShowDragGrid;

	private static (Rgba32 Color, int LineWidth) GetActiveGridStyle(ThingAppearanceOptions options) =>
		options.ShowDragGrid
			? (options.DragGridColor, Math.Max(1, options.DragGridLineWidth))
			: (options.GridColor, Math.Max(1, options.GridLineWidth));

	private static void ApplyGridAndHighlight(Image<Rgba32> canvas, ThingAppearanceOptions options, int edge, int cols, int rows)
	{
		if (UsesGrid(options))
		{
			var (gridColor, gridLineWidth) = GetActiveGridStyle(options);
			DrawGrid(canvas, edge, cols, rows, 0, 0, canvas.Width, canvas.Height, gridColor, gridLineWidth);
		}

		DrawHighlight(canvas, options);
	}

	private static void DrawHighlight(Image<Rgba32> canvas, ThingAppearanceOptions options)
	{
		if (options.HighlightRect is not { } rect)
			return;

		DrawFilledRect(canvas, rect.X, rect.Y, rect.Width, rect.Height, options.HighlightColor);
	}

	private static void DrawFilledRect(Image<Rgba32> canvas, int x, int y, int width, int height, Rgba32 color)
	{
		var right = Math.Min(x + width, canvas.Width);
		var bottom = Math.Min(y + height, canvas.Height);
		x = Math.Max(0, x);
		y = Math.Max(0, y);

		for (var py = y; py < bottom; py++)
		{
			for (var px = x; px < right; px++)
				canvas[px, py] = AlphaBlend(canvas[px, py], color);
		}
	}

	private static Rgba32 AlphaBlend(Rgba32 dst, Rgba32 src)
	{
		var srcA = src.A / 255f;
		if (srcA <= 0)
			return dst;

		var dstA = dst.A / 255f;
		var outA = srcA + dstA * (1 - srcA);
		if (outA <= 0)
			return default;

		byte Blend(byte s, byte d) => (byte)Math.Clamp((s * srcA + d * dstA * (1 - srcA)) / outA, 0, 255);
		return new Rgba32(Blend(src.R, dst.R), Blend(src.G, dst.G), Blend(src.B, dst.B), (byte)(outA * 255));
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

	private static void DrawPatternCellBorders(Image<Rgba32> canvas, int cellW, int cellH, int patternX, int patternY, (Rgba32 Color, int LineWidth) style)
	{
		var borderColor = style.Color;
		var lineWidth = style.LineWidth;
		for (var x = 1; x < patternX; x++)
		{
			var px = x * cellW;
			DrawVerticalLine(canvas, px, 0, canvas.Height, borderColor, lineWidth);
		}

		for (var y = 1; y < patternY; y++)
		{
			var py = y * cellH;
			DrawHorizontalLine(canvas, py, 0, canvas.Width, borderColor, lineWidth);
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

	private static void DrawGrid(Image<Rgba32> canvas, int edge, int cols, int rows, int offsetX, int offsetY, int clipW, int clipH, Rgba32 gridColor, int lineWidth)
	{
		var maxX = offsetX + clipW;
		var maxY = offsetY + clipH;

		for (var x = 1; x < cols; x++)
		{
			var px = offsetX + x * edge;
			if (px >= maxX)
				continue;
			DrawVerticalLine(canvas, px, offsetY, maxY, gridColor, lineWidth);
		}

		for (var y = 1; y < rows; y++)
		{
			var py = offsetY + y * edge;
			if (py >= maxY)
				continue;
			DrawHorizontalLine(canvas, py, offsetX, maxX, gridColor, lineWidth);
		}
	}

	private static void DrawVerticalLine(Image<Rgba32> canvas, int x, int yStart, int yEnd, Rgba32 color, int lineWidth)
	{
		for (var dx = 0; dx < lineWidth; dx++)
		{
			var px = x + dx;
			if (px < 0 || px >= canvas.Width)
				continue;

			for (var y = Math.Max(0, yStart); y < Math.Min(yEnd, canvas.Height); y++)
				canvas[px, y] = color;
		}
	}

	private static void DrawHorizontalLine(Image<Rgba32> canvas, int y, int xStart, int xEnd, Rgba32 color, int lineWidth)
	{
		for (var dy = 0; dy < lineWidth; dy++)
		{
			var py = y + dy;
			if (py < 0 || py >= canvas.Height)
				continue;

			for (var x = Math.Max(0, xStart); x < Math.Min(xEnd, canvas.Width); x++)
				canvas[x, py] = color;
		}
	}

	private static void DrawMissileCompassCellBorders(Image<Rgba32> canvas, int cellW, int cellH, Rgba32 borderColor, int lineWidth)
	{
		for (var row = 0; row < 3; row++)
		{
			for (var col = 0; col < 3; col++)
			{
				if (col == 1 && row == 1)
					continue;

				DrawRectBorder(canvas, col * cellW, row * cellH, cellW, cellH, borderColor, lineWidth);
			}
		}
	}

	private static void DrawRectBorder(Image<Rgba32> canvas, int x, int y, int width, int height, Rgba32 color, int lineWidth = 1)
	{
		var right = Math.Min(x + width, canvas.Width);
		var bottom = Math.Min(y + height, canvas.Height);
		x = Math.Clamp(x, 0, canvas.Width - 1);
		y = Math.Clamp(y, 0, canvas.Height - 1);

		DrawHorizontalLine(canvas, y, x, right, color, lineWidth);
		if (bottom > y)
			DrawHorizontalLine(canvas, bottom - lineWidth, x, right, color, lineWidth);

		DrawVerticalLine(canvas, x, y, bottom, color, lineWidth);
		if (right > x)
			DrawVerticalLine(canvas, right - lineWidth, y, bottom, color, lineWidth);
	}

	private static void DrawGrid(Image<Rgba32> canvas, int edge, int cols, int rows)
	{
		DrawGrid(canvas, edge, cols, rows, 0, 0, canvas.Width, canvas.Height, new Rgba32(80, 80, 80, 180), 1);
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
