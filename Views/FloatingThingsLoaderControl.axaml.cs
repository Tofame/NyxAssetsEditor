using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Platform.Storage;
using System;
using NyxAssetsEditor.ViewModels;

namespace NyxAssetsEditor.Views
{
	public partial class FloatingThingsLoaderControl : UserControl
	{
		private bool _isDragging;
		private Point _clickPosition;
		private Point _dragStartPosition;
		private bool _dragThresholdMet;
		private bool _isResizing;
		private int _resizeDirection; // 1 = Right, 2 = Bottom, 3 = Corner, 4 = Left
		private Point _initialPointerPosition;
		private double _initialWidth;
		private double _initialHeight;
		private double _initialPositionX;
		private IPointer? _activePointer;
		private static IPointer? _sharedActivePointer;
		private const double DragThreshold = 8.0;

		public FloatingThingsLoaderControl()
		{
			InitializeComponent();
			
			var titleBar = this.FindControl<Border>("TitleBar");
			if (titleBar != null)
			{
				titleBar.PointerPressed += OnTitleBarPointerPressed;
				titleBar.PointerMoved += OnTitleBarPointerMoved;
				titleBar.PointerReleased += OnTitleBarPointerReleased;
			}
		}

		protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnAttachedToVisualTree(e);

			if (DataContext is FloatingThingsLoaderViewModel vm)
			{
				if (vm.PositionX == 100)
				{
					vm.PositionX = 20;
				}

				if (vm.IsDraggingVM && _sharedActivePointer != null)
				{
					_isDragging = true;
					_activePointer = _sharedActivePointer;
					_clickPosition = new Point(vm.DragClickX, vm.DragClickY);

					var titleBar = this.FindControl<Border>("TitleBar");
					if (titleBar != null)
					{
						_activePointer.Capture(titleBar);
					}
				}
			}
		}

		private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
		{
			if (e.Source is Visual visual)
			{
				var current = visual;
				while (current != null && current != this)
				{
					if (current is Button || current is ComboBox || current is TextBox)
					{
						return;
					}
					current = current.GetVisualParent();
				}
			}

			if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && DataContext is FloatingThingsLoaderViewModel vm)
			{
				_isDragging = true;
				_dragThresholdMet = false;
				_activePointer = e.Pointer;
				_sharedActivePointer = e.Pointer;
				_clickPosition = e.GetPosition(this);
				_dragStartPosition = e.GetPosition(this);

				vm.IsDraggingVM = true;
				vm.DragClickX = _clickPosition.X;
				vm.DragClickY = _clickPosition.Y;

				e.Pointer.Capture(sender as IInputElement);
				e.Handled = true;
			}
		}

		private void OnTitleBarPointerMoved(object? sender, PointerEventArgs e)
		{
			if (!_isDragging || DataContext is not FloatingThingsLoaderViewModel vm) return;

			var assetsView = this.FindAncestorOfType<AssetsView>();
			var parentVm = assetsView?.DataContext as AssetsViewModel;

			// Check drag threshold before committing any undock
			if (!_dragThresholdMet)
			{
				var curPos = e.GetPosition(this);
				var dx = curPos.X - _dragStartPosition.X;
				var dy = curPos.Y - _dragStartPosition.Y;
				if (Math.Sqrt(dx * dx + dy * dy) < DragThreshold) return;
				_dragThresholdMet = true;
				if (parentVm != null) parentVm.IsDraggingPanel = true;
			}

			// If still docked, undock now (threshold already met)
			if (!vm.IsFloating)
			{
				vm.DockState = "Floating";
				if (assetsView != null)
				{
					var pos = e.GetPosition(assetsView);
					vm.PositionX = pos.X - _clickPosition.X;
					vm.PositionY = pos.Y - _clickPosition.Y;
				}
				e.Handled = true;
				return;
			}

			// Move freely on canvas
			Visual? canvasVisual = GetParentCanvas();
			if (canvasVisual != null)
			{
				var currentPosition = e.GetPosition(canvasVisual);
				vm.PositionX = currentPosition.X - _clickPosition.X;
				vm.PositionY = currentPosition.Y - _clickPosition.Y;
			}

			// Highlight the drop target the cursor is currently over (hittest)
			if (parentVm != null && assetsView != null)
			{
				var cursorInView = e.GetPosition(assetsView);
				parentVm.DragOverZone = GetDropZoneFromPoint(assetsView, cursorInView);
			}

			e.Handled = true;
		}

		private void OnTitleBarPointerReleased(object? sender, PointerReleasedEventArgs e)
		{
			if (!_isDragging || DataContext is not FloatingThingsLoaderViewModel vm) return;

			_isDragging = false;
			_dragThresholdMet = false;
			_activePointer = null;
			_sharedActivePointer = null;
			vm.IsDraggingVM = false;
			e.Pointer.Capture(null);
			e.Handled = true;

			var assetsView = this.FindAncestorOfType<AssetsView>();
			if (assetsView != null && assetsView.DataContext is AssetsViewModel parentVm)
			{
				// Dock only if released over an explicit drop target
				var cursorInView = e.GetPosition(assetsView);
				var hitZone = GetDropZoneFromPoint(assetsView, cursorInView);
				if (hitZone != null)
				{
					vm.DockState = hitZone;
				}
				parentVm.DragOverZone = null;
				parentVm.IsDraggingPanel = false;
				parentVm.TriggerSaveAppState();
			}
		}

		/// <summary>Checks if a point (in AssetsView coords) is within any named drop-target border.</summary>
		private static string? GetDropZoneFromPoint(AssetsView assetsView, Point cursorInView)
		{
			var targets = new (string name, string zone)[]
			{
				("LeftDropTarget",   "Left"),
				("CenterDropTarget", "Center"),
				("RightDropTarget",  "Right"),
			};
			foreach (var (name, zone) in targets)
			{
				var border = assetsView.FindControl<Border>(name);
				if (border == null || !border.IsVisible) continue;
				var topLeft = border.TranslatePoint(new Point(0, 0), assetsView);
				if (topLeft == null) continue;
				var rect = new Rect(topLeft.Value, border.Bounds.Size);
				if (rect.Contains(cursorInView)) return zone;
			}
			return null;
		}

		public async void OnEmptyStateClick(object? sender, PointerPressedEventArgs e)
		{
			if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && DataContext is FloatingThingsLoaderViewModel vm)
			{
				var topLevel = TopLevel.GetTopLevel(this);
				if (topLevel == null) return;

				var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
				{
					Title = "Open Nyx Things or Dat Archive",
					AllowMultiple = false,
					FileTypeFilter = new[]
					{
						new FilePickerFileType("Nyx Dat Archive") { Patterns = new[] { "*.dat" } },
						new FilePickerFileType("Nyx Things Archive") { Patterns = new[] { "*.things" } },
						new FilePickerFileType("All Supported Archives") { Patterns = new[] { "*.dat", "*.things" } }
					}
				});

				if (files != null && files.Count > 0)
				{
					var filePath = files[0].Path.LocalPath;
					vm.LoadArchive(filePath);
				}
				
				e.Handled = true;
			}
		}

		private void OnResizeLeftPressed(object? sender, PointerPressedEventArgs e)
		{
			StartResizing(sender, e, 4);
		}

		private void OnResizeRightPressed(object? sender, PointerPressedEventArgs e)
		{
			StartResizing(sender, e, 1);
		}

		private void OnResizeBottomPressed(object? sender, PointerPressedEventArgs e)
		{
			StartResizing(sender, e, 2);
		}

		private void OnResizeCornerPressed(object? sender, PointerPressedEventArgs e)
		{
			StartResizing(sender, e, 3);
		}

		private void OnResizeBottomLeftPressed(object? sender, PointerPressedEventArgs e)
		{
			StartResizing(sender, e, 5);
		}

		private void StartResizing(object? sender, PointerPressedEventArgs e, int direction)
		{
			if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && DataContext is FloatingThingsLoaderViewModel vm && vm.IsFloating)
			{
				_isResizing = true;
				_resizeDirection = direction;
				
				var canvasVisual = GetParentCanvas();
				if (canvasVisual != null)
				{
					_initialPointerPosition = e.GetPosition(canvasVisual);
					_initialWidth = vm.PanelWidth;
					_initialHeight = vm.ContentHeight;
					_initialPositionX = vm.PositionX;
					e.Pointer.Capture(sender as IInputElement);
					e.Handled = true;
				}
			}
		}

		private void OnResizeLeftMoved(object? sender, PointerEventArgs e)
		{
			PerformResizing(e);
		}

		private void OnResizeRightMoved(object? sender, PointerEventArgs e)
		{
			PerformResizing(e);
		}

		private void OnResizeBottomMoved(object? sender, PointerEventArgs e)
		{
			PerformResizing(e);
		}

		private void OnResizeCornerMoved(object? sender, PointerEventArgs e)
		{
			PerformResizing(e);
		}

		private void OnResizeBottomLeftMoved(object? sender, PointerEventArgs e)
		{
			PerformResizing(e);
		}

		private void PerformResizing(PointerEventArgs e)
		{
			if (_isResizing && DataContext is FloatingThingsLoaderViewModel vm)
			{
				var canvasVisual = GetParentCanvas();
				if (canvasVisual != null)
				{
					var currentPos = e.GetPosition(canvasVisual);
					var dx = currentPos.X - _initialPointerPosition.X;
					var dy = currentPos.Y - _initialPointerPosition.Y;

					if (_resizeDirection == 1 || _resizeDirection == 3)
					{
						vm.PanelWidth = Math.Max(340, _initialWidth + dx);
					}
					
					if (_resizeDirection == 4 || _resizeDirection == 5)
					{
						double newWidth = Math.Max(340, _initialWidth - dx);
						double widthDiff = newWidth - _initialWidth;
						vm.PanelWidth = newWidth;
						vm.PositionX = _initialPositionX - widthDiff;
					}
					
					if (_resizeDirection == 2 || _resizeDirection == 3 || _resizeDirection == 5)
					{
						vm.ContentHeight = Math.Max(150, _initialHeight + dy);
					}
					e.Handled = true;
				}
			}
		}

		private void OnResizeReleased(object? sender, PointerReleasedEventArgs e)
		{
			if (_isResizing)
			{
				_isResizing = false;
				e.Pointer.Capture(null);
				e.Handled = true;

				var assetsView = this.FindAncestorOfType<AssetsView>();
				if (assetsView != null && assetsView.DataContext is AssetsViewModel parentVm)
				{
					parentVm.TriggerSaveAppState();
				}
			}
		}

		private Visual? GetParentCanvas()
		{
			Visual? canvasVisual = this;
			while (canvasVisual != null && canvasVisual is not Canvas)
			{
				canvasVisual = canvasVisual.GetVisualParent();
			}
			return canvasVisual;
		}
	}
}
