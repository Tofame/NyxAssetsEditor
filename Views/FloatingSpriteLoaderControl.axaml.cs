using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Platform.Storage;
using System;
using NyxAssetsEditor.ViewModels;

namespace NyxAssetsEditor.Views
{
	public partial class FloatingSpriteLoaderControl : UserControl
	{
		private bool _isDragging;
		private Point _clickPosition;
		private bool _isResizing;
		private int _resizeDirection; // 1 = Right, 2 = Bottom, 3 = Corner, 4 = Left
		private Point _initialPointerPosition;
		private double _initialWidth;
		private double _initialHeight;
		private double _initialPositionX;
		private FloatingSpriteLoaderViewModel? _viewModel;
		private IPointer? _activePointer;
		private static IPointer? _sharedActivePointer;

		public FloatingSpriteLoaderControl()
		{
			InitializeComponent();
			
			var titleBar = this.FindControl<Border>("TitleBar");
			if (titleBar != null)
			{
				titleBar.PointerPressed += OnTitleBarPointerPressed;
				titleBar.PointerMoved += OnTitleBarPointerMoved;
				titleBar.PointerReleased += OnTitleBarPointerReleased;
			}

			this.DataContextChanged += (s, e) =>
			{
				if (_viewModel != null)
				{
					_viewModel.RequestSaveAs -= OnSaveAsRequested;
				}
				_viewModel = DataContext as FloatingSpriteLoaderViewModel;
				if (_viewModel != null)
				{
					_viewModel.RequestSaveAs += OnSaveAsRequested;
				}
			};
		}

		protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnAttachedToVisualTree(e);

			if (DataContext is FloatingSpriteLoaderViewModel vm)
			{
				if (vm.PositionX == 100)
				{
					var canvasVisual = GetParentCanvas();
					if (canvasVisual != null)
					{
						double canvasWidth = canvasVisual.Bounds.Width;
						if (canvasWidth > 0)
						{
							vm.PositionX = canvasWidth - vm.PanelWidth - 20;
						}
						else
						{
							vm.PositionX = 600;
						}
					}
					else
					{
						vm.PositionX = 600;
					}
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

			if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && DataContext is FloatingSpriteLoaderViewModel vm)
			{
				_isDragging = true;
				_activePointer = e.Pointer;
				_sharedActivePointer = e.Pointer;
				_clickPosition = e.GetPosition(this);

				vm.IsDraggingVM = true;
				vm.DragClickX = _clickPosition.X;
				vm.DragClickY = _clickPosition.Y;

				e.Pointer.Capture(sender as IInputElement);
				e.Handled = true;
			}
		}

		private void OnTitleBarPointerMoved(object? sender, PointerEventArgs e)
		{
			if (_isDragging && DataContext is FloatingSpriteLoaderViewModel vm)
			{
				Visual? canvasVisual = GetParentCanvas();
				if (canvasVisual != null)
				{
					if (!vm.IsFloating)
					{
						vm.DockState = "Floating";
					}
					var currentPosition = e.GetPosition(canvasVisual);
					vm.PositionX = currentPosition.X - _clickPosition.X;
					vm.PositionY = currentPosition.Y - _clickPosition.Y;

					var assetsView = this.FindAncestorOfType<AssetsView>();
					if (assetsView != null && assetsView.DataContext is AssetsViewModel parentVm)
					{
						double viewWidth = assetsView.Bounds.Width;
						var relativePos = e.GetPosition(assetsView);
						if (relativePos.X < viewWidth * 0.25)
						{
							parentVm.DragOverZone = "Left";
						}
						else if (relativePos.X > viewWidth * 0.75)
						{
							parentVm.DragOverZone = "Right";
						}
						else
						{
							parentVm.DragOverZone = "Center";
						}
					}
				}
				e.Handled = true;
			}
		}

		private void OnTitleBarPointerReleased(object? sender, PointerReleasedEventArgs e)
		{
			if (_isDragging && DataContext is FloatingSpriteLoaderViewModel vm)
			{
				_isDragging = false;
				_activePointer = null;
				_sharedActivePointer = null;
				vm.IsDraggingVM = false;
				e.Pointer.Capture(null);
				e.Handled = true;

				var assetsView = this.FindAncestorOfType<AssetsView>();
				if (assetsView != null && assetsView.DataContext is AssetsViewModel parentVm)
				{
					if (parentVm.DragOverZone != null)
					{
						vm.DockState = parentVm.DragOverZone;
					}
					parentVm.DragOverZone = null;
				}
			}
		}

		public async void OnEmptyStateClick(object? sender, PointerPressedEventArgs e)
		{
			if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && DataContext is FloatingSpriteLoaderViewModel vm)
			{
				var topLevel = TopLevel.GetTopLevel(this);
				if (topLevel == null) return;

				var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
				{
					Title = "Open Nyx Sprite or Asset Archive",
					AllowMultiple = false,
					FileTypeFilter = new[]
					{
						new FilePickerFileType("Nyx Sprite Archive") { Patterns = new[] { "*.spr" } },
						new FilePickerFileType("Nyx Asset Archive") { Patterns = new[] { "*.assets" } },
						new FilePickerFileType("All Supported Archives") { Patterns = new[] { "*.spr", "*.assets" } }
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
			if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && DataContext is FloatingSpriteLoaderViewModel vm && vm.IsFloating)
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
			if (_isResizing && DataContext is FloatingSpriteLoaderViewModel vm)
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

		private async void OnSaveAsRequested(object? sender, EventArgs e)
		{
			if (DataContext is FloatingSpriteLoaderViewModel vm)
			{
				var topLevel = TopLevel.GetTopLevel(this);
				if (topLevel != null)
				{
					var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
					{
						Title = "Save Archive As",
						DefaultExtension = System.IO.Path.GetExtension(vm.FilePath),
						SuggestedFileName = System.IO.Path.GetFileName(vm.FilePath),
						FileTypeChoices = new[]
						{
							new FilePickerFileType("Nyx Sprite Archive") { Patterns = new[] { "*.spr" } },
							new FilePickerFileType("Nyx Asset Archive") { Patterns = new[] { "*.assets" } },
							new FilePickerFileType("All Supported Archives") { Patterns = new[] { "*.spr", "*.assets" } }
						}
					});

					if (file != null)
					{
						try
						{
							System.IO.File.Copy(vm.FilePath, file.Path.LocalPath, true);
							vm.FilePath = file.Path.LocalPath;
						}
						catch (Exception ex)
						{
							System.Diagnostics.Debug.WriteLine($"Failed to save as: {ex.Message}");
						}
					}
				}
			}
		}
	}
}
