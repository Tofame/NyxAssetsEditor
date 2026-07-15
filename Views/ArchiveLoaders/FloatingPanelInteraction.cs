using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using NyxAssetsEditor.ViewModels.Core;
using NyxAssetsEditor.ViewModels.Pages;

namespace NyxAssetsEditor.Views.ArchiveLoaders;

/// <summary>Shared drag, dock, and resize behavior for floating archive/editor panels.</summary>
public sealed class FloatingPanelInteraction
{
	private static IPointer? _sharedActivePointer;

	private readonly UserControl _host;
	private readonly Border _titleBar;
	private readonly double _minWidth;
	private readonly double _minHeight;

	private bool _isDragging;
	private Point _clickPosition;
	private Point _dragStartPosition;
	private bool _dragThresholdMet;
	private bool _isResizing;
	private int _resizeDirection;
	private Point _initialPointerPosition;
	private double _initialWidth;
	private double _initialHeight;
	private double _initialPositionX;
	private double _initialPositionY;
	private IPointer? _activePointer;
	private IInputElement? _activeDragElement;

	private const double DragThreshold = 8.0;

	public FloatingPanelInteraction(UserControl host, Border titleBar, Border? bottomBar = null, double minWidth = 340, double minHeight = 150)
	{
		_host = host;
		_titleBar = titleBar;
		_minWidth = minWidth;
		_minHeight = minHeight;

		_titleBar.PointerPressed += OnTitleBarPointerPressed;
		_titleBar.PointerMoved += OnTitleBarPointerMoved;
		_titleBar.PointerReleased += OnTitleBarPointerReleased;
		_host.AttachedToVisualTree += OnAttachedToVisualTree;

		if (bottomBar != null)
		{
			bottomBar.PointerPressed += OnTitleBarPointerPressed;
			bottomBar.PointerMoved += OnTitleBarPointerMoved;
			bottomBar.PointerReleased += OnTitleBarPointerReleased;
		}
	}

	public void RegisterResizeHandle(Border handle, int direction)
	{
		handle.PointerPressed += (s, e) => StartResizing(handle, e, direction);
		handle.PointerMoved += (_, e) => PerformResizing(e);
		handle.PointerReleased += OnResizeReleased;
	}

	private PanelViewModelBase? Vm => _host.DataContext as PanelViewModelBase;

	private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
	{
		if (Vm is not { IsDraggingVM: true } vm || _sharedActivePointer == null)
			return;

		_isDragging = true;
		_activePointer = _sharedActivePointer;
		_clickPosition = new Point(vm.DragClickX, vm.DragClickY);
		_activePointer.Capture(_activeDragElement ?? _titleBar);
	}

	private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (e.Source is Visual visual)
		{
			var current = visual;
			while (current != null && current != _host)
			{
				if (current is Button or ComboBox or TextBox)
					return;
				current = current.GetVisualParent();
			}
		}

		if (!e.GetCurrentPoint(_host).Properties.IsLeftButtonPressed || Vm == null)
			return;

		_isDragging = true;
		_dragThresholdMet = false;
		_activePointer = e.Pointer;
		_sharedActivePointer = e.Pointer;
		_clickPosition = e.GetPosition(_host);
		_dragStartPosition = _clickPosition;

		Vm.IsDraggingVM = true;
		Vm.DragClickX = _clickPosition.X;
		Vm.DragClickY = _clickPosition.Y;

		_activeDragElement = sender as IInputElement;
		e.Pointer.Capture(_activeDragElement);
		e.Handled = true;
	}


	private void OnTitleBarPointerMoved(object? sender, PointerEventArgs e)
	{
		var vm = Vm;
		if (!_isDragging || vm == null)
			return;

		var pointerPoint = e.GetCurrentPoint(_host);
		if (!pointerPoint.Properties.IsLeftButtonPressed)
		{
			_isDragging = false;
			_dragThresholdMet = false;
			_activePointer = null;
			_sharedActivePointer = null;
			_activeDragElement = null;
			vm.IsDraggingVM = false;
			e.Pointer.Capture(null);
			e.Handled = true;

			var assetsView2 = _host.FindAncestorOfType<Pages.AssetsView>();
			if (assetsView2?.DataContext is AssetsViewModel parentVm2)
			{
				parentVm2.DragOverZone = null;
				parentVm2.IsDraggingPanel = false;
				parentVm2.TriggerSaveAppState();
			}
			return;
		}

		var assetsView = _host.FindAncestorOfType<Pages.AssetsView>();
		var parentVm = assetsView?.DataContext as AssetsViewModel;

		if (!_dragThresholdMet)
		{
			var curPos = e.GetPosition(_host);
			var dx = curPos.X - _dragStartPosition.X;
			var dy = curPos.Y - _dragStartPosition.Y;
			if (Math.Sqrt(dx * dx + dy * dy) < DragThreshold)
				return;

			_dragThresholdMet = true;
			parentVm?.IsDraggingPanel = true;
		}

		if (!vm.IsFloating)
		{
			vm.DockState = "Floating";
			if (assetsView != null)
			{
				var pos = e.GetPosition(assetsView);
				vm.PositionX = pos.X - _clickPosition.X;
				vm.PositionY = Math.Max(0, pos.Y - _clickPosition.Y);
			}

			e.Handled = true;
			return;
		}

		var canvasVisual = GetParentCanvas();
		if (canvasVisual != null)
		{
			var currentPosition = e.GetPosition(canvasVisual);
			vm.PositionX = currentPosition.X - _clickPosition.X;
			vm.PositionY = Math.Max(0, currentPosition.Y - _clickPosition.Y);
		}

		if (parentVm != null && assetsView != null)
		{
			var cursorInView = e.GetPosition(assetsView);
			parentVm.DragOverZone = GetDropZoneFromPoint(assetsView, cursorInView);
		}

		e.Handled = true;
	}

	private void OnTitleBarPointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		var vm = Vm;
		if (!_isDragging || vm == null)
			return;

		_isDragging = false;
		_dragThresholdMet = false;
		_activePointer = null;
		_sharedActivePointer = null;
		_activeDragElement = null;
		vm.IsDraggingVM = false;
		e.Pointer.Capture(null);
		e.Handled = true;

		var assetsView = _host.FindAncestorOfType<Pages.AssetsView>();
		if (assetsView?.DataContext is not AssetsViewModel parentVm)
			return;

		var cursorInView = e.GetPosition(assetsView);
		var hitZone = GetDropZoneFromPoint(assetsView, cursorInView);
		if (hitZone != null)
			vm.DockState = hitZone;

		parentVm.DragOverZone = null;
		parentVm.IsDraggingPanel = false;
		parentVm.TriggerSaveAppState();
	}

	private void StartResizing(IInputElement handle, PointerPressedEventArgs e, int direction)
	{
		if (!e.GetCurrentPoint(_host).Properties.IsLeftButtonPressed || Vm is not { IsFloating: true } vm)
			return;

		var canvasVisual = GetParentCanvas();
		if (canvasVisual == null)
			return;

		_isResizing = true;
		_resizeDirection = direction;
		_initialPointerPosition = e.GetPosition(canvasVisual);
		_initialWidth = vm.PanelWidth;
		_initialHeight = vm.ContentHeight;
		_initialPositionX = vm.PositionX;
		_initialPositionY = vm.PositionY;
		e.Pointer.Capture(handle);
		e.Handled = true;
	}

	private void PerformResizing(PointerEventArgs e)
	{
		if (!_isResizing || Vm == null)
			return;

		var canvasVisual = GetParentCanvas();
		if (canvasVisual == null)
			return;

		var currentPos = e.GetPosition(canvasVisual);
		var dx = currentPos.X - _initialPointerPosition.X;
		var dy = currentPos.Y - _initialPointerPosition.Y;

		if (_resizeDirection is 1 or 3 or 7)
			Vm.PanelWidth = Math.Max(_minWidth, _initialWidth + dx);

		if (_resizeDirection is 4 or 5 or 8)
		{
			var newWidth = Math.Max(_minWidth, _initialWidth - dx);
			var widthDiff = newWidth - _initialWidth;
			Vm.PanelWidth = newWidth;
			Vm.PositionX = _initialPositionX - widthDiff;
		}

		if (_resizeDirection is 2 or 3 or 5)
			Vm.ContentHeight = Math.Max(_minHeight, _initialHeight + dy);

		if (_resizeDirection is 6 or 7 or 8)
		{
			var newHeight = Math.Max(_minHeight, _initialHeight - dy);
			var heightDiff = newHeight - _initialHeight;
			Vm.ContentHeight = newHeight;
			Vm.PositionY = Math.Max(0, _initialPositionY - heightDiff);
		}

		e.Handled = true;
	}

	private void OnResizeReleased(object? sender, PointerReleasedEventArgs e)
	{
		if (!_isResizing)
			return;

		_isResizing = false;
		e.Pointer.Capture(null);
		e.Handled = true;

		var assetsView = _host.FindAncestorOfType<Pages.AssetsView>();
		if (assetsView?.DataContext is AssetsViewModel parentVm)
			parentVm.TriggerSaveAppState();
	}

	private Visual? GetParentCanvas()
	{
		Visual? canvasVisual = _host;
		while (canvasVisual != null && canvasVisual is not Canvas)
			canvasVisual = canvasVisual.GetVisualParent();
		return canvasVisual;
	}

	private static string? GetDropZoneFromPoint(Pages.AssetsView assetsView, Point cursorInView)
	{
		var targets = new (string name, string zone)[]
		{
			("LeftDropTarget", "Left"),
			("CenterDropTarget", "Center"),
			("RightDropTarget", "Right"),
		};

		foreach (var (name, zone) in targets)
		{
			var border = assetsView.FindControl<Border>(name);
			if (border == null || !border.IsVisible)
				continue;

			var topLeft = border.TranslatePoint(new Point(0, 0), assetsView);
			if (topLeft == null)
				continue;

			var rect = new Rect(topLeft.Value, border.Bounds.Size);
			if (rect.Contains(cursorInView))
				return zone;
		}

		return null;
	}
}
