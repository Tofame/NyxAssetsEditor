using System;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NyxAssetsEditor.ViewModels.Pages;

namespace NyxAssetsEditor.Views.Common;

public sealed class SpritesheetCanvasControl : Control
{
	private const double RulerSize = 24;
	private SpritesheetSlicerViewModel? _viewModel;
	private bool _dragging;
	private Point _dragOffset;

	public SpritesheetCanvasControl()
	{
		Focusable = true;
		RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
		DataContextChanged += OnDataContextChanged;
	}

	protected override Size MeasureOverride(Size availableSize)
	{
		if (_viewModel?.HasImage != true) return new Size(640, 480);
		return new Size(RulerSize + _viewModel.ImageWidth * _viewModel.Zoom, RulerSize + _viewModel.ImageHeight * _viewModel.Zoom);
	}

	public override void Render(DrawingContext context)
	{
		base.Render(context);
		context.DrawRectangle(new SolidColorBrush(Color.Parse("#181818")), null, new Rect(Bounds.Size));
		var vm = _viewModel;
		if (vm?.SheetBitmap == null)
		{
			var empty = new FormattedText("Open or drop a spritesheet", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 16, Brushes.Gray);
			context.DrawText(empty, new Point(32, 32));
			return;
		}

		var zoom = vm.Zoom;
		var imageRect = new Rect(RulerSize, RulerSize, vm.ImageWidth * zoom, vm.ImageHeight * zoom);
		var checker = Math.Max(4, 8 * zoom);
		for (double y = imageRect.Y; y < imageRect.Bottom; y += checker)
		for (double x = imageRect.X; x < imageRect.Right; x += checker)
		{
			var dark = (((int)((x - imageRect.X) / checker) + (int)((y - imageRect.Y) / checker)) & 1) == 0;
			context.DrawRectangle(dark ? Brushes.DimGray : Brushes.DarkGray, null,
				new Rect(x, y, Math.Min(checker, imageRect.Right - x), Math.Min(checker, imageRect.Bottom - y)));
		}
		context.DrawImage(vm.SheetBitmap, new Rect(0, 0, vm.ImageWidth, vm.ImageHeight), imageRect);

		DrawRulers(context, vm, imageRect);
		DrawGrid(context, vm, zoom);
	}

	private static void DrawRulers(DrawingContext context, SpritesheetSlicerViewModel vm, Rect imageRect)
	{
		var rulerBrush = new SolidColorBrush(Color.Parse("#252525"));
		var tickPen = new Pen(new SolidColorBrush(Color.Parse("#888888")), 1);
		context.DrawRectangle(rulerBrush, null, new Rect(0, 0, imageRect.Right, RulerSize));
		context.DrawRectangle(rulerBrush, null, new Rect(0, 0, RulerSize, imageRect.Bottom));
		var step = 8;
		while (step * vm.Zoom < 48) step *= 2;
		for (var pixel = 0; pixel <= vm.ImageWidth; pixel += step)
		{
			var x = RulerSize + pixel * vm.Zoom;
			context.DrawLine(tickPen, new Point(x, RulerSize - 6), new Point(x, RulerSize));
			var label = new FormattedText(pixel.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 9, Brushes.LightGray);
			context.DrawText(label, new Point(x + 2, 2));
		}
		for (var pixel = 0; pixel <= vm.ImageHeight; pixel += step)
		{
			var y = RulerSize + pixel * vm.Zoom;
			context.DrawLine(tickPen, new Point(RulerSize - 6, y), new Point(RulerSize, y));
			var label = new FormattedText(pixel.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 9, Brushes.LightGray);
			using (context.PushTransform(Matrix.CreateRotation(-Math.PI / 2) * Matrix.CreateTranslation(2, y - 2)))
				context.DrawText(label, new Point());
		}
	}

	private static void DrawGrid(DrawingContext context, SpritesheetSlicerViewModel vm, double zoom)
	{
		if (vm.Columns <= 0 || vm.Rows <= 0) return;
		var x = RulerSize + vm.OffsetX * zoom;
		var y = RulerSize + vm.OffsetY * zoom;
		var cell = vm.CellSize * zoom;
		var width = vm.Columns * cell;
		var height = vm.Rows * cell;
		var gridPen = new Pen(new SolidColorBrush(Color.Parse("#E6FF4D7A")), Math.Max(1, zoom < 1 ? 1 : 1.5));
		context.DrawRectangle(new SolidColorBrush(Color.Parse("#183A7BD5")), gridPen, new Rect(x, y, width, height));
		for (var column = 1; column < vm.Columns; column++)
		{
			var px = x + column * cell;
			context.DrawLine(gridPen, new Point(px, y), new Point(px, y + height));
		}
		for (var row = 1; row < vm.Rows; row++)
		{
			var py = y + row * cell;
			context.DrawLine(gridPen, new Point(x, py), new Point(x + width, py));
		}
	}

	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);
		var vm = _viewModel;
		if (vm?.HasImage != true || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
		Focus();
		var point = e.GetPosition(this);
		var sheetX = (point.X - RulerSize) / vm.Zoom;
		var sheetY = (point.Y - RulerSize) / vm.Zoom;
		if (sheetX < 0 || sheetY < 0 || sheetX > vm.ImageWidth || sheetY > vm.ImageHeight) return;

		var insideSelection = sheetX >= vm.OffsetX && sheetY >= vm.OffsetY &&
			sheetX <= vm.OffsetX + vm.Columns * vm.CellSize &&
			sheetY <= vm.OffsetY + vm.Rows * vm.CellSize;
		if (!insideSelection)
		{
			// Clicking elsewhere on the sheet re-centres the selection, matching the
			// quick positioning behavior artists expect from the classic slicer.
			vm.MoveGridTo(
				(int)Math.Round(sheetX - vm.Columns * vm.CellSize / 2d),
				(int)Math.Round(sheetY - vm.Rows * vm.CellSize / 2d));
		}
		_dragging = true;
		_dragOffset = new Point(sheetX - vm.OffsetX, sheetY - vm.OffsetY);
		e.Pointer.Capture(this); e.Handled = true;
	}

	protected override void OnPointerMoved(PointerEventArgs e)
	{
		base.OnPointerMoved(e);
		if (!_dragging || _viewModel == null) return;
		var point = e.GetPosition(this);
		var x = (int)Math.Round((point.X - RulerSize) / _viewModel.Zoom - _dragOffset.X);
		var y = (int)Math.Round((point.Y - RulerSize) / _viewModel.Zoom - _dragOffset.Y);
		_viewModel.MoveGridTo(x, y); e.Handled = true;
	}

	protected override void OnPointerReleased(PointerReleasedEventArgs e)
	{
		base.OnPointerReleased(e);
		if (!_dragging) return;
		_dragging = false; e.Pointer.Capture(null); e.Handled = true;
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		if (_viewModel == null) return;
		var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
		switch (e.Key)
		{
			case Key.Left: _viewModel.NudgeGrid(-1, 0); e.Handled = true; break;
			case Key.Right: _viewModel.NudgeGrid(1, 0); e.Handled = true; break;
			case Key.Up: _viewModel.NudgeGrid(0, -1); e.Handled = true; break;
			case Key.Down: _viewModel.NudgeGrid(0, 1); e.Handled = true; break;
			case Key.Enter when !control && _viewModel.CropCommand.CanExecute(null): _viewModel.CropCommand.Execute(null); e.Handled = true; break;
			case Key.OemPlus when control: _viewModel.ZoomIn(); e.Handled = true; break;
			case Key.OemMinus when control: _viewModel.ZoomOut(); e.Handled = true; break;
		}
	}

	private void OnDataContextChanged(object? sender, EventArgs e)
	{
		if (_viewModel != null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
		_viewModel = DataContext as SpritesheetSlicerViewModel;
		if (_viewModel != null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;
		InvalidateMeasure(); InvalidateVisual();
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(SpritesheetSlicerViewModel.Zoom) or nameof(SpritesheetSlicerViewModel.ImageWidth) or nameof(SpritesheetSlicerViewModel.ImageHeight)) InvalidateMeasure();
		InvalidateVisual();
	}
}
