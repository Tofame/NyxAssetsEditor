using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;

namespace NyxAssetsEditor.Services.ImportExport;

public sealed record SlicerImage(int Width, int Height, byte[] Rgba)
{
	public SlicerImage Copy() => new(Width, Height, (byte[])Rgba.Clone());
}

public readonly record struct SlicerGrid(int X, int Y, int Columns, int Rows, int CellSize)
{
	public int PixelWidth => Columns * CellSize;
	public int PixelHeight => Rows * CellSize;
}

public sealed record SlicerCell(int Column, int Row, byte[] Rgba, bool IsEmpty);

public sealed record GridDetectionResult(bool Success, SlicerGrid Grid, string Message)
{
	public static GridDetectionResult Failed(string message) => new(false, default, message);
}

/// <summary>Pure pixel and grid operations used by the spritesheet slicer.</summary>
public static class SpritesheetSlicerService
{
	public static SlicerImage Load(string path)
	{
		using var bitmap = SKBitmap.Decode(path) ?? throw new InvalidOperationException("The selected image could not be decoded.");
		var info = new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
		using var converted = new SKBitmap(info);
		using (var canvas = new SKCanvas(converted))
		{
			canvas.Clear(SKColors.Transparent);
			canvas.DrawBitmap(bitmap, 0, 0);
		}
		return new SlicerImage(converted.Width, converted.Height, converted.Bytes);
	}

	public static SlicerGrid ClampGrid(SlicerGrid grid, int imageWidth, int imageHeight)
	{
		var cell = Math.Max(1, grid.CellSize);
		if (imageWidth < cell || imageHeight < cell)
			return new SlicerGrid(0, 0, 0, 0, cell);

		var columns = Math.Clamp(grid.Columns, 1, imageWidth / cell);
		var rows = Math.Clamp(grid.Rows, 1, imageHeight / cell);
		var x = Math.Clamp(grid.X, 0, imageWidth - columns * cell);
		var y = Math.Clamp(grid.Y, 0, imageHeight - rows * cell);
		return new SlicerGrid(x, y, columns, rows, cell);
	}

	public static IReadOnlyList<SlicerCell> Slice(SlicerImage image, SlicerGrid requestedGrid, bool includeEmpty)
	{
		var grid = ClampGrid(requestedGrid, image.Width, image.Height);
		var result = new List<SlicerCell>();
		if (grid.Columns == 0 || grid.Rows == 0)
			return result;

		for (var row = 0; row < grid.Rows; row++)
		{
			for (var column = 0; column < grid.Columns; column++)
			{
				var pixels = CopyCell(image, grid.X + column * grid.CellSize, grid.Y + row * grid.CellSize, grid.CellSize);
				NormalizeMagentaInPlace(pixels);
				var empty = IsEmpty(pixels);
				if (includeEmpty || !empty)
					result.Add(new SlicerCell(column, row, pixels, empty));
			}
		}
		return result;
	}

	public static byte[] CopyCell(SlicerImage image, int x, int y, int cellSize)
	{
		if (x < 0 || y < 0 || cellSize <= 0 || x + cellSize > image.Width || y + cellSize > image.Height)
			throw new ArgumentOutOfRangeException(nameof(x), "The requested cell is outside the image.");

		var result = new byte[cellSize * cellSize * 4];
		var bytesPerRow = cellSize * 4;
		for (var row = 0; row < cellSize; row++)
			Buffer.BlockCopy(image.Rgba, ((y + row) * image.Width + x) * 4, result, row * bytesPerRow, bytesPerRow);
		return result;
	}

	public static bool IsEmpty(ReadOnlySpan<byte> rgba)
	{
		for (var i = 3; i < rgba.Length; i += 4)
			if (rgba[i] != 0)
				return false;
		return true;
	}

	public static void NormalizeMagentaInPlace(Span<byte> rgba)
	{
		for (var i = 0; i + 3 < rgba.Length; i += 4)
		{
			if (rgba[i] == 255 && rgba[i + 1] == 0 && rgba[i + 2] == 255 && rgba[i + 3] == 255)
			{
				rgba[i] = 0;
				rgba[i + 1] = 0;
				rgba[i + 2] = 0;
				rgba[i + 3] = 0;
			}
		}
	}

	public static SlicerImage RotateClockwise(SlicerImage source)
	{
		var output = new byte[source.Rgba.Length];
		for (var y = 0; y < source.Height; y++)
		for (var x = 0; x < source.Width; x++)
			CopyPixel(source.Rgba, (y * source.Width + x) * 4, output, (x * source.Height + (source.Height - 1 - y)) * 4);
		return new SlicerImage(source.Height, source.Width, output);
	}

	public static SlicerImage RotateCounterClockwise(SlicerImage source)
	{
		var output = new byte[source.Rgba.Length];
		for (var y = 0; y < source.Height; y++)
		for (var x = 0; x < source.Width; x++)
			CopyPixel(source.Rgba, (y * source.Width + x) * 4, output, ((source.Width - 1 - x) * source.Height + y) * 4);
		return new SlicerImage(source.Height, source.Width, output);
	}

	public static SlicerImage FlipHorizontal(SlicerImage source)
	{
		var output = new byte[source.Rgba.Length];
		for (var y = 0; y < source.Height; y++)
		for (var x = 0; x < source.Width; x++)
			CopyPixel(source.Rgba, (y * source.Width + x) * 4, output, (y * source.Width + source.Width - 1 - x) * 4);
		return new SlicerImage(source.Width, source.Height, output);
	}

	public static SlicerImage FlipVertical(SlicerImage source)
	{
		var output = new byte[source.Rgba.Length];
		for (var y = 0; y < source.Height; y++)
		for (var x = 0; x < source.Width; x++)
			CopyPixel(source.Rgba, (y * source.Width + x) * 4, output, ((source.Height - 1 - y) * source.Width + x) * 4);
		return new SlicerImage(source.Width, source.Height, output);
	}

	public static SlicerImage FillTransparentWithMagenta(SlicerImage source)
	{
		var output = (byte[])source.Rgba.Clone();
		for (var i = 0; i + 3 < output.Length; i += 4)
		{
			if (output[i + 3] == 0)
			{
				output[i] = 255;
				output[i + 1] = 0;
				output[i + 2] = 255;
				output[i + 3] = 255;
			}
		}
		return new SlicerImage(source.Width, source.Height, output);
	}

	public static GridDetectionResult DetectGrid(SlicerImage image, IReadOnlyList<int> supportedCellSizes)
	{
		if (supportedCellSizes.Count == 0)
			return GridDetectionResult.Failed("No project sprite sizes are available.");

		var normalized = (byte[])image.Rgba.Clone();
		NormalizeMagentaInPlace(normalized);
		if (IsEmpty(normalized))
			return GridDetectionResult.Failed("The image contains no visible pixels.");

		var candidates = new List<(double Score, SlicerGrid Grid)>();
		foreach (var cell in supportedCellSizes.Where(c => c > 0 && c <= image.Width && c <= image.Height).Distinct())
		{
			var xScores = ScoreOffsets(normalized, image.Width, image.Height, cell, vertical: true);
			var yScores = ScoreOffsets(normalized, image.Width, image.Height, cell, vertical: false);
			foreach (var xs in xScores.Take(2))
			foreach (var ys in yScores.Take(2))
			{
				var occupied = FindOccupiedCells(normalized, image.Width, image.Height, xs.Offset, ys.Offset, cell);
				if (occupied == null) continue;
				var value = occupied.Value;
				var occupiedCount = (value.MaxColumn - value.MinColumn + 1) * (value.MaxRow - value.MinRow + 1);
				var edgeFitBonus = ((image.Width - xs.Offset) % cell == 0 ? 0.2 : 0) + ((image.Height - ys.Offset) % cell == 0 ? 0.2 : 0);
				var coverageBonus = Math.Min(0.4, occupiedCount * 0.02);
				candidates.Add((xs.Score + ys.Score + edgeFitBonus + coverageBonus, new SlicerGrid(
					xs.Offset + value.MinColumn * cell,
					ys.Offset + value.MinRow * cell,
					value.MaxColumn - value.MinColumn + 1,
					value.MaxRow - value.MinRow + 1,
					cell)));
			}
		}

		var ordered = candidates.OrderByDescending(c => c.Score).ToList();
		if (ordered.Count == 0 || ordered[0].Score < 1.2)
			return GridDetectionResult.Failed("No consistent empty row/column separators were found.");
		if (ordered.Count > 1 && Math.Abs(ordered[0].Score - ordered[1].Score) < 0.05 && ordered[0].Grid != ordered[1].Grid)
			return GridDetectionResult.Failed("Grid detection is ambiguous. Align the grid manually or remove extra transparent gutters.");

		return new GridDetectionResult(true, ClampGrid(ordered[0].Grid, image.Width, image.Height), "Grid proposal detected. Review it before cropping.");
	}

	public static string ExportPng(byte[] rgba, int size, string directory, string baseName, int index)
		=> ExportImage(rgba, size, directory, baseName, index, "png");

	public static string ExportImage(byte[] rgba, int size, string directory, string baseName, int index, string format)
	{
		Directory.CreateDirectory(directory);
		var extension = ResolveExtension(format);
		var encodedFormat = ResolveEncodedFormat(format);
		var safeBase = string.IsNullOrWhiteSpace(baseName) ? "sprite" : string.Concat(baseName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
		var candidate = Path.Combine(directory, $"{safeBase}_{index:0000}{extension}");
		for (var suffix = 2; File.Exists(candidate); suffix++)
			candidate = Path.Combine(directory, $"{safeBase}_{index:0000}_{suffix}{extension}");

		var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul);
		using var bitmap = new SKBitmap(info);
		System.Runtime.InteropServices.Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
		using var image = SKImage.FromBitmap(bitmap);
		using var encoded = image.Encode(encodedFormat, 100)
			?? throw new InvalidOperationException($"Failed to encode cropped sprite as {extension.TrimStart('.')}.");
		using var stream = File.Create(candidate);
		encoded.SaveTo(stream);
		return candidate;
	}

	private static string ResolveExtension(string format) => format.ToLowerInvariant() switch
	{
		"jpg" or "jpeg" => ".jpg",
		"bmp" => ".bmp",
		_ => ".png",
	};

	private static SKEncodedImageFormat ResolveEncodedFormat(string format) => format.ToLowerInvariant() switch
	{
		"jpg" or "jpeg" => SKEncodedImageFormat.Jpeg,
		"bmp" => SKEncodedImageFormat.Bmp,
		_ => SKEncodedImageFormat.Png,
	};

	private static IReadOnlyList<(int Offset, double Score)> ScoreOffsets(byte[] rgba, int width, int height, int cell, bool vertical)
	{
		var limit = vertical ? width : height;
		var result = new List<(int, double)>();
		for (var offset = 0; offset < Math.Min(cell, limit); offset++)
		{
			var matches = 0;
			var total = 0;
			for (var position = offset + cell; position < limit; position += cell)
			{
				total++;
				if (IsAxisEmpty(rgba, width, height, position, vertical)) matches++;
			}
			if (total > 0) result.Add((offset, (double)matches / total));
		}
		return result.OrderByDescending(x => x.Item2).ThenBy(x => x.Item1).Select(x => (x.Item1, x.Item2)).ToList();
	}

	private static bool IsAxisEmpty(byte[] rgba, int width, int height, int position, bool vertical)
	{
		if (vertical)
		{
			for (var y = 0; y < height; y++) if (rgba[(y * width + position) * 4 + 3] != 0) return false;
		}
		else
		{
			for (var x = 0; x < width; x++) if (rgba[(position * width + x) * 4 + 3] != 0) return false;
		}
		return true;
	}

	private static (int MinColumn, int MinRow, int MaxColumn, int MaxRow)? FindOccupiedCells(byte[] rgba, int width, int height, int offsetX, int offsetY, int cell)
	{
		var columns = (width - offsetX) / cell;
		var rows = (height - offsetY) / cell;
		var minColumn = int.MaxValue; var minRow = int.MaxValue; var maxColumn = -1; var maxRow = -1;
		for (var row = 0; row < rows; row++)
		for (var column = 0; column < columns; column++)
		{
			var occupied = false;
			for (var y = 0; y < cell && !occupied; y++)
			for (var x = 0; x < cell; x++)
				if (rgba[((offsetY + row * cell + y) * width + offsetX + column * cell + x) * 4 + 3] != 0) { occupied = true; break; }
			if (!occupied) continue;
			minColumn = Math.Min(minColumn, column); minRow = Math.Min(minRow, row);
			maxColumn = Math.Max(maxColumn, column); maxRow = Math.Max(maxRow, row);
		}
		return maxColumn < 0 ? null : (minColumn, minRow, maxColumn, maxRow);
	}

	private static void CopyPixel(byte[] source, int sourceOffset, byte[] destination, int destinationOffset) =>
		Buffer.BlockCopy(source, sourceOffset, destination, destinationOffset, 4);
}
