using System;
using System.Linq;
using NyxAssets.Sprites;
using NyxAssets.Things;
using NyxAssets.Things.Frames;
using NyxAssets.Utils;
using NyxAssetsEditor.Services.Archive;
using SkiaSharp;

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
	public SKColor GridColor { get; init; } = new(80, 80, 80, 180);
	public int GridLineWidth { get; init; } = 1;
	public SKColor DragGridColor { get; init; } = new(255, 105, 180, 180);
	public int DragGridLineWidth { get; init; } = 1;
	public SKColor HighlightColor { get; init; } = new(58, 123, 213, 128);
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
		var canvas = new byte[canvasW * canvasH * 4];

		DrawFrameGroupCell(canvas, canvasW, canvasH, fg, loader, options, 0, 0);

		ApplyGridAndHighlight(canvas, canvasW, canvasH, options, edge, (int)fg.Width, (int)fg.Height);

		if (options.ShowCropSize && fg.ExactSize > 0 && fg.ExactSize < edge)
			DrawCropRect(canvas, canvasW, canvasH, (int)fg.ExactSize, canvasW, canvasH);

		return canvas;
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
		var canvas = new byte[canvasW * canvasH * 4];

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
				DrawFrameGroupCell(canvas, canvasW, canvasH, fg, loader, patternOptions, offsetX, offsetY);

				if (options.ShowCropSize && fg.ExactSize > 0 && fg.ExactSize < edge)
					DrawCropRect(canvas, canvasW, canvasH, (int)fg.ExactSize, cellW, cellH, offsetX, offsetY);
			}
		}



		ApplyGridAndHighlight(canvas, canvasW, canvasH, options, edge, (int)(fg.PatternX * fg.Width), (int)(fg.PatternY * fg.Height));
		if (UsesGrid(options))
			DrawPatternCellBorders(canvas, canvasW, canvasH, cellW, cellH, (int)fg.PatternX, (int)fg.PatternY, GetActiveGridStyle(options));

		return canvas;
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
		var canvas = new byte[canvasW * canvasH * 4];

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
			DrawFrameGroupCell(canvas, canvasW, canvasH, fg, loader, cellOptions, offsetX, offsetY);

			if (options.ShowCropSize && fg.ExactSize > 0 && fg.ExactSize < edge)
				DrawCropRect(canvas, canvasW, canvasH, (int)fg.ExactSize, cellW, cellH, offsetX, offsetY);
		}



		var (borderColor, borderWidth) = UsesGrid(options)
			? GetActiveGridStyle(options)
			: (new SKColor(90, 90, 90, 220), 1);
		DrawMissileCompassCellBorders(canvas, canvasW, canvasH, cellW, cellH, borderColor, borderWidth);

		if (UsesGrid(options))
		{
			var (gridColor, gridLineWidth) = GetActiveGridStyle(options);
			foreach (var (_, column, row) in slots)
			{
				var offsetX = column * cellW;
				var offsetY = row * cellH;
				DrawGrid(canvas, canvasW, canvasH, edge, (int)fg.Width, (int)fg.Height, offsetX, offsetY, cellW, cellH, gridColor, gridLineWidth);
			}
		}

		DrawHighlight(canvas, canvasW, canvasH, options);

		return canvas;
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

		var canvas = new byte[canvasW * canvasH * 4];
		var edge = SpritePixelCodec.SpriteEdgeLength;

		if (isMissile)
		{
			var cellW = (int)(fg.Width * edge);
			var cellH = (int)(fg.Height * edge);
			var (borderColor, borderWidth) = GetActiveGridStyle(options);
			DrawMissileCompassCellBorders(canvas, canvasW, canvasH, cellW, cellH, borderColor, borderWidth);

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
				DrawGrid(canvas, canvasW, canvasH, edge, (int)fg.Width, (int)fg.Height, offsetX, offsetY, cellW, cellH, borderColor, borderWidth);
			}
		}
		else if (showPatternGrid)
		{
			var cellW = (int)(fg.Width * edge);
			var cellH = (int)(fg.Height * edge);
			ApplyGridAndHighlight(canvas, canvasW, canvasH, options, edge, (int)(fg.PatternX * fg.Width), (int)(fg.PatternY * fg.Height));
			DrawPatternCellBorders(canvas, canvasW, canvasH, cellW, cellH, (int)fg.PatternX, (int)fg.PatternY, GetActiveGridStyle(options));
		}
		else
		{
			ApplyGridAndHighlight(canvas, canvasW, canvasH, options, edge, (int)fg.Width, (int)fg.Height);
		}

		DrawHighlight(canvas, canvasW, canvasH, options);
		return canvas;
	}

	private static bool UsesGrid(ThingAppearanceOptions options) => options.ShowGrid || options.ShowDragGrid;

	private static (SKColor Color, int LineWidth) GetActiveGridStyle(ThingAppearanceOptions options) =>
		options.ShowDragGrid
			? (options.DragGridColor, Math.Max(1, options.DragGridLineWidth))
			: (options.GridColor, Math.Max(1, options.GridLineWidth));

	private static void ApplyGridAndHighlight(byte[] canvas, int width, int height, ThingAppearanceOptions options, int edge, int cols, int rows)
	{
		if (UsesGrid(options))
		{
			var (gridColor, gridLineWidth) = GetActiveGridStyle(options);
			DrawGrid(canvas, width, height, edge, cols, rows, 0, 0, width, height, gridColor, gridLineWidth);
		}

		DrawHighlight(canvas, width, height, options);
	}

	private static void DrawHighlight(byte[] canvas, int width, int height, ThingAppearanceOptions options)
	{
		if (options.HighlightRect is not { } rect)
			return;

		DrawFilledRect(canvas, width, height, rect.X, rect.Y, rect.Width, rect.Height, options.HighlightColor);
	}

	private static void DrawFilledRect(byte[] canvas, int canvasW, int canvasH, int x, int y, int width, int height, SKColor color)
	{
		var right = Math.Min(x + width, canvasW);
		var bottom = Math.Min(y + height, canvasH);
		x = Math.Max(0, x);
		y = Math.Max(0, y);

		for (var py = y; py < bottom; py++)
		{
			for (var px = x; px < right; px++)
			{
				var offset = (py * canvasW + px) * 4;
				var dst = new SKColor(canvas[offset], canvas[offset + 1], canvas[offset + 2], canvas[offset + 3]);
				var blended = AlphaBlend(dst, color);
				canvas[offset] = blended.Red;
				canvas[offset + 1] = blended.Green;
				canvas[offset + 2] = blended.Blue;
				canvas[offset + 3] = blended.Alpha;
			}
		}
	}

	private static SKColor AlphaBlend(SKColor dst, SKColor src)
	{
		var srcA = src.Alpha / 255f;
		if (srcA <= 0)
			return dst;

		var dstA = dst.Alpha / 255f;
		var outA = srcA + dstA * (1 - srcA);
		if (outA <= 0)
			return SKColors.Transparent;

		byte Blend(byte s, byte d) => (byte)Math.Clamp((s * srcA + d * dstA * (1 - srcA)) / outA, 0, 255);
		return new SKColor(Blend(src.Red, dst.Red), Blend(src.Green, dst.Green), Blend(src.Blue, dst.Blue), (byte)(outA * 255));
	}

	private static bool DrawFrameGroupCell(
		byte[] canvas,
		int canvasW,
		int canvasH,
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
				BlitSpriteBuffer(canvas, canvasW, canvasH, innerX, innerY, pixels);
				drewAny = true;
			}
		}

		return drewAny;
	}

	private static void DrawPatternCellBorders(byte[] canvas, int canvasW, int canvasH, int cellW, int cellH, int patternX, int patternY, (SKColor Color, int LineWidth) style)
	{
		var borderColor = style.Color;
		var lineWidth = style.LineWidth;
		for (var x = 1; x < patternX; x++)
		{
			var px = x * cellW;
			DrawVerticalLine(canvas, canvasW, canvasH, px, 0, canvasH, borderColor, lineWidth);
		}

		for (var y = 1; y < patternY; y++)
		{
			var py = y * cellH;
			DrawHorizontalLine(canvas, canvasW, canvasH, py, 0, canvasW, borderColor, lineWidth);
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

	private static void DrawGrid(byte[] canvas, int canvasW, int canvasH, int edge, int cols, int rows, int offsetX, int offsetY, int clipW, int clipH, SKColor gridColor, int lineWidth)
	{
		var maxX = offsetX + clipW;
		var maxY = offsetY + clipH;

		for (var x = 1; x < cols; x++)
		{
			var px = offsetX + x * edge;
			if (px >= maxX)
				continue;
			DrawVerticalLine(canvas, canvasW, canvasH, px, offsetY, maxY, gridColor, lineWidth);
		}

		for (var y = 1; y < rows; y++)
		{
			var py = offsetY + y * edge;
			if (py >= maxY)
				continue;
			DrawHorizontalLine(canvas, canvasW, canvasH, py, offsetX, maxX, gridColor, lineWidth);
		}
	}

	private static void DrawVerticalLine(byte[] canvas, int canvasW, int canvasH, int x, int yStart, int yEnd, SKColor color, int lineWidth)
	{
		for (var dx = 0; dx < lineWidth; dx++)
		{
			var px = x + dx;
			if (px < 0 || px >= canvasW)
				continue;

			for (var y = Math.Max(0, yStart); y < Math.Min(yEnd, canvasH); y++)
			{
				var offset = (y * canvasW + px) * 4;
				canvas[offset] = color.Red;
				canvas[offset + 1] = color.Green;
				canvas[offset + 2] = color.Blue;
				canvas[offset + 3] = color.Alpha;
			}
		}
	}

	private static void DrawHorizontalLine(byte[] canvas, int canvasW, int canvasH, int y, int xStart, int xEnd, SKColor color, int lineWidth)
	{
		for (var dy = 0; dy < lineWidth; dy++)
		{
			var py = y + dy;
			if (py < 0 || py >= canvasH)
				continue;

			for (var x = Math.Max(0, xStart); x < Math.Min(xEnd, canvasW); x++)
			{
				var offset = (py * canvasW + x) * 4;
				canvas[offset] = color.Red;
				canvas[offset + 1] = color.Green;
				canvas[offset + 2] = color.Blue;
				canvas[offset + 3] = color.Alpha;
			}
		}
	}

	private static void DrawMissileCompassCellBorders(byte[] canvas, int canvasW, int canvasH, int cellW, int cellH, SKColor borderColor, int lineWidth)
	{
		for (var row = 0; row < 3; row++)
		{
			for (var col = 0; col < 3; col++)
			{
				if (col == 1 && row == 1)
					continue;

				DrawRectBorder(canvas, canvasW, canvasH, col * cellW, row * cellH, cellW, cellH, borderColor, lineWidth);
			}
		}
	}

	private static void DrawRectBorder(byte[] canvas, int canvasW, int canvasH, int x, int y, int width, int height, SKColor color, int lineWidth = 1)
	{
		var right = Math.Min(x + width, canvasW);
		var bottom = Math.Min(y + height, canvasH);
		x = Math.Clamp(x, 0, canvasW - 1);
		y = Math.Clamp(y, 0, canvasH - 1);

		DrawHorizontalLine(canvas, canvasW, canvasH, y, x, right, color, lineWidth);
		if (bottom > y)
			DrawHorizontalLine(canvas, canvasW, canvasH, bottom - lineWidth, x, right, color, lineWidth);

		DrawVerticalLine(canvas, canvasW, canvasH, x, y, bottom, color, lineWidth);
		if (right > x)
			DrawVerticalLine(canvas, canvasW, canvasH, right - lineWidth, y, bottom, color, lineWidth);
	}

	private static void DrawGrid(byte[] canvas, int canvasW, int canvasH, int edge, int cols, int rows)
	{
		DrawGrid(canvas, canvasW, canvasH, edge, cols, rows, 0, 0, canvasW, canvasH, new SKColor(80, 80, 80, 180), 1);
	}

	private static void DrawCropRect(byte[] canvas, int canvasW, int canvasH, int exactSize, int clipW, int clipH, int offsetX = 0, int offsetY = 0)
	{
		var cropColor = new SKColor(80, 220, 80, 220);
		var left = offsetX + (clipW - exactSize) / 2;
		var top = offsetY + (clipH - exactSize) / 2;
		var right = left + exactSize - 1;
		var bottom = top + exactSize - 1;

		for (var x = left; x <= right && x < canvasW; x++)
		{
			var topOffset = (Math.Clamp(top, 0, canvasH - 1) * canvasW + x) * 4;
			canvas[topOffset] = cropColor.Red;
			canvas[topOffset + 1] = cropColor.Green;
			canvas[topOffset + 2] = cropColor.Blue;
			canvas[topOffset + 3] = cropColor.Alpha;

			var bottomOffset = (Math.Clamp(bottom, 0, canvasH - 1) * canvasW + x) * 4;
			canvas[bottomOffset] = cropColor.Red;
			canvas[bottomOffset + 1] = cropColor.Green;
			canvas[bottomOffset + 2] = cropColor.Blue;
			canvas[bottomOffset + 3] = cropColor.Alpha;
		}

		for (var y = top; y <= bottom && y < canvasH; y++)
		{
			var leftOffset = (y * canvasW + Math.Clamp(left, 0, canvasW - 1)) * 4;
			canvas[leftOffset] = cropColor.Red;
			canvas[leftOffset + 1] = cropColor.Green;
			canvas[leftOffset + 2] = cropColor.Blue;
			canvas[leftOffset + 3] = cropColor.Alpha;

			var rightOffset = (y * canvasW + Math.Clamp(right, 0, canvasW - 1)) * 4;
			canvas[rightOffset] = cropColor.Red;
			canvas[rightOffset + 1] = cropColor.Green;
			canvas[rightOffset + 2] = cropColor.Blue;
			canvas[rightOffset + 3] = cropColor.Alpha;
		}
	}

	private static void BlitSpriteBuffer(byte[] dst, int dstW, int dstH, int x, int y, byte[] src)
	{
		var edge = SpritePixelCodec.SpriteEdgeLength;
		for (var sy = 0; sy < edge; sy++)
		{
			var dy = y + sy;
			if (dy < 0 || dy >= dstH) continue;
			for (var sx = 0; sx < edge; sx++)
			{
				var dx = x + sx;
				if (dx < 0 || dx >= dstW) continue;

				var srcOffset = (sy * edge + sx) * 4;
				var dstOffset = (dy * dstW + dx) * 4;

				var srcA = src[srcOffset + 3];
				if (srcA == 0)
					continue;

				if (srcA == 255)
				{
					dst[dstOffset] = src[srcOffset];
					dst[dstOffset + 1] = src[srcOffset + 1];
					dst[dstOffset + 2] = src[srcOffset + 2];
					dst[dstOffset + 3] = src[srcOffset + 3];
				}
				else
				{
					var sA = srcA / 255f;
					var dA = dst[dstOffset + 3] / 255f;
					var outA = sA + dA * (1 - sA);
					if (outA > 0)
					{
						dst[dstOffset] = (byte)Math.Clamp((src[srcOffset] * sA + dst[dstOffset] * dA * (1 - sA)) / outA, 0, 255);
						dst[dstOffset + 1] = (byte)Math.Clamp((src[srcOffset + 1] * sA + dst[dstOffset + 1] * dA * (1 - sA)) / outA, 0, 255);
						dst[dstOffset + 2] = (byte)Math.Clamp((src[srcOffset + 2] * sA + dst[dstOffset + 2] * dA * (1 - sA)) / outA, 0, 255);
						dst[dstOffset + 3] = (byte)(outA * 255);
					}
				}
			}
		}
	}
}
