using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NyxAssetsEditor.ViewModels.Common;
using NyxAssetsEditor.Services.Rendering;
using SkiaSharp;

namespace NyxAssetsEditor.Services.ImportExport;

public sealed record SlicerImage(int Width, int Height, byte[] Rgba);

public readonly record struct SlicerGrid(int X, int Y, int Columns, int Rows, int CellSize)
{
	public int PixelWidth => Columns * CellSize;
	public int PixelHeight => Rows * CellSize;
}

[Flags]
public enum SlicerResizeEdges
{
	None = 0,
	Left = 1,
	Right = 2,
	Top = 4,
	Bottom = 8
}

public sealed record SlicerCell(int Column, int Row, byte[] Rgba, bool IsEmpty);

public sealed record GridDetectionResult(bool Success, SlicerGrid Grid, string Message)
{
	public static GridDetectionResult Failed(string message) => new(false, default, message);
}

public sealed record SlicerHistoryState(SlicerImage Image, SlicerGrid Grid, string Action);

/// <summary>
/// Bounded undo/redo history for immutable slicer image versions and their grid state.
/// Recording a new action after undoing clears the redo branch.
/// </summary>
public sealed class SlicerHistory
{
	private readonly int _capacity;
	private readonly List<SlicerHistoryState> _undo = new();
	private readonly List<SlicerHistoryState> _redo = new();

	public SlicerHistory(int capacity)
	{
		if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
		_capacity = capacity;
	}

	public bool CanUndo => _undo.Count > 0;
	public bool CanRedo => _redo.Count > 0;
	public int UndoCount => _undo.Count;
	public int RedoCount => _redo.Count;

	public void Record(SlicerImage image, SlicerGrid grid, string action)
	{
		ArgumentNullException.ThrowIfNull(image);
		if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("Describe the history action.", nameof(action));
		Push(_undo, new SlicerHistoryState(image, grid, action));
		_redo.Clear();
	}

	public SlicerHistoryState Undo(SlicerImage currentImage, SlicerGrid currentGrid)
	{
		ArgumentNullException.ThrowIfNull(currentImage);
		if (!CanUndo) throw new InvalidOperationException("There is no slicer action to undo.");
		var previous = Pop(_undo);
		Push(_redo, new SlicerHistoryState(currentImage, currentGrid, previous.Action));
		return previous;
	}

	public SlicerHistoryState Redo(SlicerImage currentImage, SlicerGrid currentGrid)
	{
		ArgumentNullException.ThrowIfNull(currentImage);
		if (!CanRedo) throw new InvalidOperationException("There is no slicer action to redo.");
		var next = Pop(_redo);
		Push(_undo, new SlicerHistoryState(currentImage, currentGrid, next.Action));
		return next;
	}

	public void Clear()
	{
		_undo.Clear();
		_redo.Clear();
	}

	private void Push(List<SlicerHistoryState> history, SlicerHistoryState state)
	{
		history.Add(state);
		if (history.Count > _capacity) history.RemoveAt(0);
	}

	private static SlicerHistoryState Pop(List<SlicerHistoryState> history)
	{
		var index = history.Count - 1;
		var state = history[index];
		history.RemoveAt(index);
		return state;
	}
}

/// <summary>Pure pixel and grid operations used by the spritesheet slicer.</summary>
public static class SpritesheetSlicerService
{
	public static double RecommendZoom(int width, int height)
	{
		if (width > 0 && height > 0 && width <= 128 && height <= 128) return 4;
		if (width > 0 && height > 0 && width < 256 && height < 256) return 2;
		return 1;
	}

	public static int RecommendExactSize(int widthCells, int heightCells, int cellSize)
	{
		if (widthCells <= 0) throw new ArgumentOutOfRangeException(nameof(widthCells));
		if (heightCells <= 0) throw new ArgumentOutOfRangeException(nameof(heightCells));
		if (cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize));
		return (int)Math.Clamp((long)Math.Max(widthCells, heightCells) * cellSize, 1, byte.MaxValue);
	}

	public static SlicerImage Load(string path)
	{
		using var bitmap = SKBitmap.Decode(path) ?? throw new InvalidOperationException("The selected image could not be decoded.");
		return LoadFromBitmap(bitmap);
	}

	public static SlicerImage LoadFromStream(Stream stream)
	{
		ArgumentNullException.ThrowIfNull(stream);
		using var bitmap = SKBitmap.Decode(stream) ?? throw new InvalidOperationException("The clipboard image could not be decoded.");
		return LoadFromBitmap(bitmap);
	}

	private static SlicerImage LoadFromBitmap(SKBitmap bitmap)
	{
		var info = new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
		using var converted = new SKBitmap(info);
		using (var canvas = new SKCanvas(converted))
		{
			canvas.Clear(SKColors.Transparent);
			canvas.DrawBitmap(bitmap, 0, 0, SKSamplingOptions.Default);
		}
		return RemoveOpaqueMagenta(new SlicerImage(converted.Width, converted.Height, converted.Bytes));
	}

	public static SlicerImage RemoveOpaqueMagenta(SlicerImage source)
	{
		var pixels = (byte[])source.Rgba.Clone();
		NormalizeMagentaInPlace(pixels);
		return new SlicerImage(source.Width, source.Height, pixels);
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

	public static int QuantizeDragDelta(double delta, int cellSize, bool snapToGrid)
	{
		var step = snapToGrid ? Math.Max(1, cellSize) : 1;
		return checked((int)Math.Round(delta / step, MidpointRounding.AwayFromZero) * step);
	}

	public static SlicerGrid ResizeGridFromDrag(
		SlicerGrid startingGrid,
		SlicerResizeEdges edges,
		double deltaX,
		double deltaY,
		int imageWidth,
		int imageHeight)
	{
		var grid = ClampGrid(startingGrid, imageWidth, imageHeight);
		if (grid.Columns <= 0 || grid.Rows <= 0 || edges == SlicerResizeEdges.None) return grid;

		var x = grid.X;
		var y = grid.Y;
		var columns = grid.Columns;
		var rows = grid.Rows;
		var columnDelta = checked((int)Math.Round(deltaX / grid.CellSize, MidpointRounding.AwayFromZero));
		var rowDelta = checked((int)Math.Round(deltaY / grid.CellSize, MidpointRounding.AwayFromZero));

		if (edges.HasFlag(SlicerResizeEdges.Left))
		{
			var applied = Math.Clamp(columnDelta, -(x / grid.CellSize), columns - 1);
			x += applied * grid.CellSize;
			columns -= applied;
		}
		else if (edges.HasFlag(SlicerResizeEdges.Right))
		{
			var available = (imageWidth - (x + columns * grid.CellSize)) / grid.CellSize;
			columns += Math.Clamp(columnDelta, -(columns - 1), available);
		}

		if (edges.HasFlag(SlicerResizeEdges.Top))
		{
			var applied = Math.Clamp(rowDelta, -(y / grid.CellSize), rows - 1);
			y += applied * grid.CellSize;
			rows -= applied;
		}
		else if (edges.HasFlag(SlicerResizeEdges.Bottom))
		{
			var available = (imageHeight - (y + rows * grid.CellSize)) / grid.CellSize;
			rows += Math.Clamp(rowDelta, -(rows - 1), available);
		}

		return new SlicerGrid(x, y, columns, rows, grid.CellSize);
	}

	public static IReadOnlyList<SlicerCell> Slice(SlicerImage image, SlicerGrid requestedGrid, bool includeEmpty)
	{
		var grid = ClampGrid(requestedGrid, image.Width, image.Height);
		var result = new List<SlicerCell>();
		if (grid.Columns == 0 || grid.Rows == 0)
			return result;

		// Thing sheets use column-major slot order: top-to-bottom within each column,
		// then left-to-right. Keep this independent from preview display order.
		// Thing packing is coordinate-based and separately uses GetSpriteIndex.
		for (var column = 0; column < grid.Columns; column++)
		{
			for (var row = 0; row < grid.Rows; row++)
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
		var output = SpriteTransformUtil.RotateRgba90(source.Rgba, source.Width, source.Height, 1);
		return new SlicerImage(source.Height, source.Width, output);
	}

	public static SlicerImage RotateCounterClockwise(SlicerImage source)
	{
		var output = SpriteTransformUtil.RotateRgba90(source.Rgba, source.Width, source.Height, 3);
		return new SlicerImage(source.Height, source.Width, output);
	}

	public static SlicerImage FlipHorizontal(SlicerImage source)
	{
		var output = SpriteTransformUtil.FlipHorizontal(source.Rgba, source.Width, source.Height);
		return new SlicerImage(source.Width, source.Height, output);
	}

	public static SlicerImage FlipVertical(SlicerImage source)
	{
		var output = SpriteTransformUtil.FlipVertical(source.Rgba, source.Width, source.Height);
		return new SlicerImage(source.Width, source.Height, output);
	}

	public static SlicerImage TransformCells(
		SlicerImage source,
		SlicerGrid requestedGrid,
		Func<SlicerImage, SlicerImage> operation)
	{
		ArgumentNullException.ThrowIfNull(operation);
		var grid = ClampGrid(requestedGrid, source.Width, source.Height);
		if (grid.Columns <= 0 || grid.Rows <= 0)
			throw new InvalidOperationException("Select at least one complete sprite cell to transform.");

		var output = (byte[])source.Rgba.Clone();
		var bytesPerRow = grid.CellSize * 4;
		for (var column = 0; column < grid.Columns; column++)
		for (var row = 0; row < grid.Rows; row++)
		{
			var x = grid.X + column * grid.CellSize;
			var y = grid.Y + row * grid.CellSize;
			var cell = new SlicerImage(grid.CellSize, grid.CellSize, CopyCell(source, x, y, grid.CellSize));
			var transformed = operation(cell);
			if (transformed.Width != grid.CellSize || transformed.Height != grid.CellSize ||
				transformed.Rgba.Length != cell.Rgba.Length)
				throw new InvalidOperationException("A selected-cell transform must preserve the sprite cell dimensions.");

			for (var pixelRow = 0; pixelRow < grid.CellSize; pixelRow++)
				Buffer.BlockCopy(
					transformed.Rgba, pixelRow * bytesPerRow,
					output, ((y + pixelRow) * source.Width + x) * 4,
					bytesPerRow);
		}

		return new SlicerImage(source.Width, source.Height, output);
	}

	public static (SlicerImage Image, SlicerGrid Grid) StackHorizontalFrames(
		SlicerImage source,
		SlicerGrid requestedGrid,
		int frameBlockColumns,
		int frameBlockRows,
		int frames)
	{
		ArgumentNullException.ThrowIfNull(source);
		if (frameBlockColumns <= 0) throw new ArgumentOutOfRangeException(nameof(frameBlockColumns));
		if (frameBlockRows <= 0) throw new ArgumentOutOfRangeException(nameof(frameBlockRows));
		if (frames < 2) throw new InvalidOperationException("Need at least two frames to restack.");

		var grid = ClampGrid(requestedGrid, source.Width, source.Height);
		var sourceThingColumns = checked(frameBlockColumns * frames);
		var sourceThingRows = frameBlockRows;
		if (grid.Columns <= 0 || grid.Rows <= 0 ||
			grid.Columns % sourceThingColumns != 0 || grid.Rows % sourceThingRows != 0)
			throw new InvalidOperationException(
				$"The {grid.Columns}×{grid.Rows} selection is not a horizontal strip of {frames} frames of {frameBlockColumns}×{frameBlockRows} cells.");

		var thingsAcross = grid.Columns / sourceThingColumns;
		var thingsDown = grid.Rows / sourceThingRows;
		var destThingRows = checked(frameBlockRows * frames);
		var destColumns = checked(thingsAcross * frameBlockColumns);
		var destRows = checked(thingsDown * destThingRows);
		var destWidth = checked(destColumns * grid.CellSize);
		var destHeight = checked(destRows * grid.CellSize);
		var dest = new byte[checked(destWidth * destHeight * 4)];
		var blockPixelWidth = checked(frameBlockColumns * grid.CellSize);
		var blockPixelHeight = checked(frameBlockRows * grid.CellSize);

		for (var thingRow = 0; thingRow < thingsDown; thingRow++)
		for (var thingColumn = 0; thingColumn < thingsAcross; thingColumn++)
		for (var frame = 0; frame < frames; frame++)
		{
			var sourceX = grid.X + (thingColumn * sourceThingColumns + frame * frameBlockColumns) * grid.CellSize;
			var sourceY = grid.Y + thingRow * sourceThingRows * grid.CellSize;
			var destX = thingColumn * frameBlockColumns * grid.CellSize;
			var destY = (thingRow * destThingRows + frame * frameBlockRows) * grid.CellSize;
			CopyRect(source, dest, destWidth, sourceX, sourceY, destX, destY, blockPixelWidth, blockPixelHeight);
		}

		return (new SlicerImage(destWidth, destHeight, dest), new SlicerGrid(0, 0, destColumns, destRows, grid.CellSize));
	}

	private static void CopyRect(
		SlicerImage source, byte[] dest, int destWidth,
		int sourceX, int sourceY, int destX, int destY, int width, int height)
	{
		if (sourceX < 0 || sourceY < 0 || destX < 0 || destY < 0 ||
			sourceX + width > source.Width || sourceY + height > source.Height)
			throw new ArgumentOutOfRangeException(nameof(sourceX), "The requested rectangle is outside the image.");

		var bytesPerRow = checked(width * 4);
		for (var row = 0; row < height; row++)
			Buffer.BlockCopy(
				source.Rgba, ((sourceY + row) * source.Width + sourceX) * 4,
				dest, ((destY + row) * destWidth + destX) * 4,
				bytesPerRow);
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

		// Exported thing sheets reserve every fixed-size slot. Fully transparent cells,
		// rows, and columns may carry frame-group structure and must never be trimmed.
		// A real gutter would require a separate stride value, which SlicerGrid does not
		// model, so transparency is intentionally not used as a grid signal here.
		var candidates = supportedCellSizes
			.Where(cell => cell > 0 && image.Width >= cell && image.Height >= cell)
			.Distinct()
			.Where(cell => image.Width % cell == 0 && image.Height % cell == 0)
			.Select(cell => new SlicerGrid(0, 0, image.Width / cell, image.Height / cell, cell))
			.ToList();

		if (candidates.Count == 0)
			return GridDetectionResult.Failed("The image dimensions are not an exact multiple of the selected sprite size. Set the grid offsets and dimensions manually.");
		if (candidates.Count > 1)
			return GridDetectionResult.Failed("More than one project sprite size fits this image. Select the intended sprite size before cropping.");

		return new GridDetectionResult(true, candidates[0], "The full image fits the project sprite grid. Transparent cells were preserved.");
	}

	public static string ExportPng(byte[] rgba, int size, string directory, string baseName, int index)
	{
		ArgumentNullException.ThrowIfNull(rgba);
		if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
		if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Choose an export folder.", nameof(directory));
		if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
		var expectedLength = checked(size * size * 4);
		if (rgba.Length != expectedLength)
			throw new ArgumentException($"Expected {expectedLength} RGBA bytes for a {size}×{size} sprite, but received {rgba.Length}.", nameof(rgba));

		Directory.CreateDirectory(directory);
		var invalidCharacters = Path.GetInvalidFileNameChars();
		var safeBase = string.IsNullOrWhiteSpace(baseName) ? "sprite" : string.Concat(baseName.Select(c => invalidCharacters.Contains(c) ? '_' : c));

		var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul);
		using var bitmap = new SKBitmap(info);
		System.Runtime.InteropServices.Marshal.Copy(rgba, 0, bitmap.GetPixels(), expectedLength);
		using var image = SKImage.FromBitmap(bitmap);
		using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
			?? throw new InvalidOperationException("Failed to encode cropped sprite as PNG.");

		for (var suffix = 1; ; suffix++)
		{
			var suffixText = suffix == 1 ? "" : $"_{suffix}";
			var candidate = Path.Combine(directory, $"{safeBase}_{index:0000}{suffixText}{SupportedFileFormats.ExtPng}");
			FileStream stream;
			try
			{
				stream = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None);
			}
			catch (IOException) when (File.Exists(candidate))
			{
				// Another export already owns this name; advance to the next suffix.
				continue;
			}

			try
			{
				using (stream)
					encoded.SaveTo(stream);
				return candidate;
			}
			catch
			{
				try { File.Delete(candidate); }
				catch { /* Preserve the original export error. */ }
				throw;
			}
		}
	}

}
