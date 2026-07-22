using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Data.Converters;
using System.Globalization;
using System.Collections.Generic;
using NyxAssetsEditor.ViewModels.Pages;

namespace NyxAssetsEditor.Views.Pages
{
	public partial class PaintView : UserControl
	{
		private bool _isDrawing = false;
		private bool _palettePositionInitialized = false;
		private int _selectStartX = -1;
		private int _selectStartY = -1;
		private readonly bool[,] _selectionBeforeDrag = new bool[32, 32];
		private int _moveStartX = -1;
		private int _moveStartY = -1;
		private byte[]? _moveStartPixels;
		private bool[,]? _moveStartSelectionMask;

		public PaintView()
		{
			InitializeComponent();
		}

		private void OnCanvasPointerPressed(object sender, PointerPressedEventArgs e)
		{
			var props = e.GetCurrentPoint(this).Properties;
			if (!props.IsLeftButtonPressed)
				return;
			// Only start drawing when clicking directly on the canvas image
			if (e.Source is Image img)
			{
				var vm = DataContext as PaintViewModel;
				if (vm != null)
				{
					vm.SaveHistoryState();
					_isDrawing = true;

					if (vm.ActiveTool == PaintTool.Select)
					{
						var pos = e.GetPosition(img);
						_selectStartX = (int)(pos.X / img.Bounds.Width * 32);
						_selectStartY = (int)(pos.Y / img.Bounds.Height * 32);

						Array.Copy(vm.GetSelectionMask(), _selectionBeforeDrag, _selectionBeforeDrag.Length);

						bool keepExisting = e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
						if (!keepExisting)
						{
							vm.ClearSelection();
							Array.Clear(_selectionBeforeDrag, 0, _selectionBeforeDrag.Length);
						}

						vm.ApplySelectionBox(_selectStartX, _selectStartY, _selectStartX, _selectStartY, _selectionBeforeDrag, keepExisting);
					}
					else if (vm.ActiveTool == PaintTool.Move)
					{
						var pos = e.GetPosition(img);
						_moveStartX = (int)(pos.X / img.Bounds.Width * 32);
						_moveStartY = (int)(pos.Y / img.Bounds.Height * 32);

						if (vm.ActiveLayer != null)
						{
							_moveStartPixels = new byte[vm.ActiveLayer.Pixels.Length];
							Array.Copy(vm.ActiveLayer.Pixels, _moveStartPixels, _moveStartPixels.Length);
						}
						_moveStartSelectionMask = new bool[32, 32];
						Array.Copy(vm.GetSelectionMask(), _moveStartSelectionMask, _moveStartSelectionMask.Length);
					}
					else
					{
						HandlePointer(e);
					}
				}
			}
			e.Handled = true;
		}

		private void OnCanvasPointerMoved(object sender, PointerEventArgs e)
		{
			UpdateHoverPosition(e);
			if (_isDrawing)
			{
				var vm = DataContext as PaintViewModel;
				if (vm?.ActiveTool == PaintTool.Select)
				{
					HandleSelectDrag(e);
				}
				else if (vm?.ActiveTool == PaintTool.Move)
				{
					HandleMoveDrag(e);
				}
				else
				{
					HandlePointer(e);
				}
				e.Handled = true;
			}
		}

		private void OnCanvasPointerReleased(object sender, PointerReleasedEventArgs e)
		{
			_isDrawing = false;
			e.Handled = true;
		}

		private void OnCanvasPointerEntered(object sender, PointerEventArgs e)
		{
			var vm = DataContext as PaintViewModel;
			if (vm != null)
			{
				vm.IsHovering = true;
			}
		}

		private void OnCanvasPointerExited(object sender, PointerEventArgs e)
		{
			var vm = DataContext as PaintViewModel;
			if (vm != null)
			{
				vm.IsHovering = false;
			}
		}

		private void OnPointerWheelChanged(object sender, PointerWheelEventArgs e)
		{
			if (e.KeyModifiers == KeyModifiers.Control)
			{
				var vm = DataContext as PaintViewModel;
				if (vm != null)
				{
					if (e.Delta.Y > 0)
					{
						if (vm.ZoomInCommand.CanExecute(null))
							vm.ZoomInCommand.Execute(null);
					}
					else if (e.Delta.Y < 0)
					{
						if (vm.ZoomOutCommand.CanExecute(null))
							vm.ZoomOutCommand.Execute(null);
					}
				}
				e.Handled = true;
			}
		}

		private void UpdateHoverPosition(PointerEventArgs e)
		{
			var vm = DataContext as PaintViewModel;
			if (vm == null)
				return;

			var img = this.FindControl<Image>("CanvasImage");
			if (img == null || img.Bounds.Width <= 0 || img.Bounds.Height <= 0)
				return;

			var pos = e.GetPosition(img);
			int x = (int)(pos.X / img.Bounds.Width * 32);
			int y = (int)(pos.Y / img.Bounds.Height * 32);

			if (x >= 0 && x < 32 && y >= 0 && y < 32)
			{
				vm.HoverX = x;
				vm.HoverY = y;
				vm.IsHovering = true;
			}
			else
			{
				vm.IsHovering = false;
			}
		}

		private void HandlePointer(PointerEventArgs e)
		{
			var vm = DataContext as PaintViewModel;
			if (vm == null)
				return;

			var img = this.FindControl<Image>("CanvasImage");
			if (img == null || img.Bounds.Width <= 0 || img.Bounds.Height <= 0)
				return;

			var pos = e.GetPosition(img);
			
			// Map pointer position on image bounds to 32x32 grid
			int x = (int)(pos.X / img.Bounds.Width * 32);
			int y = (int)(pos.Y / img.Bounds.Height * 32);

			if (x >= 0 && x < 32 && y >= 0 && y < 32)
			{
				var props = e.GetCurrentPoint(img).Properties;
				bool isRightClick = props.IsRightButtonPressed;
				bool keepExisting = e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
				vm.HandleCanvasClick(x, y, isRightClick, keepExisting);
			}
		}

		private void HandleSelectDrag(PointerEventArgs e)
		{
			var vm = DataContext as PaintViewModel;
			var img = this.FindControl<Image>("CanvasImage");
			if (vm == null || img == null || img.Bounds.Width <= 0 || img.Bounds.Height <= 0)
				return;

			var pos = e.GetPosition(img);
			int currentX = (int)(pos.X / img.Bounds.Width * 32);
			int currentY = (int)(pos.Y / img.Bounds.Height * 32);

			currentX = Math.Clamp(currentX, 0, 31);
			currentY = Math.Clamp(currentY, 0, 31);

			bool keepExisting = e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
			vm.ApplySelectionBox(_selectStartX, _selectStartY, currentX, currentY, _selectionBeforeDrag, keepExisting);
		}

		private void OnWorkspacePointerPressed(object? sender, PointerPressedEventArgs e)
		{
			var vm = DataContext as PaintViewModel;
			if (vm != null)
			{
				vm.ClearSelection();
			}
		}

		private void HandleMoveDrag(PointerEventArgs e)
		{
			var vm = DataContext as PaintViewModel;
			var img = this.FindControl<Image>("CanvasImage");
			if (vm == null || img == null || img.Bounds.Width <= 0 || img.Bounds.Height <= 0 || _moveStartPixels == null || _moveStartSelectionMask == null)
				return;

			var pos = e.GetPosition(img);
			int currentX = (int)(pos.X / img.Bounds.Width * 32);
			int currentY = (int)(pos.Y / img.Bounds.Height * 32);

			int dx = currentX - _moveStartX;
			int dy = currentY - _moveStartY;

			vm.ShiftLayerAndSelection(dx, dy, _moveStartPixels, _moveStartSelectionMask);
		}

		private void OnPaletteColorTapped(object sender, TappedEventArgs e)
		{
			var border = sender as Border;
			if (border?.DataContext is Color color)
			{
				var vm = DataContext as PaintViewModel;
				if (vm != null)
				{
					vm.ActiveColor = color;
				}
			}
		}

		private bool _isDraggingPanel = false;
		private Control? _draggedPanel = null;
		private Avalonia.Point _panelStartPointerPosition;
		private double _panelStartLeft;
		private double _panelStartTop;

		private void OnPanelHeaderPointerPressed(object sender, PointerPressedEventArgs e)
		{
			var header = sender as Control;
			// Walk up to find the border panel that is a direct child of the Canvas container
			var panel = header;
			while (panel != null && panel.Parent is not Canvas)
			{
				panel = panel.Parent as Control;
			}

			if (panel == null)
				return;

			// Do not initiate dragging if click originates inside an interactive control
			var current = e.Source as Avalonia.Visual;
			while (current != null && current != panel)
			{
				if (current is Button || 
					current is ComboBox || 
					current is TextBox || 
					current is Slider || 
					current is ListBox ||
					current is ScrollViewer ||
					current is ItemsControl ||
					current.GetType().Name.Contains("ColorPicker") ||
					current.GetType().Name.Contains("ComboBox") ||
					current.GetType().Name.Contains("TextBox") ||
					current.GetType().Name.Contains("Button") ||
					current.GetType().Name.Contains("Slider") ||
					current.GetType().Name.Contains("ListBox") ||
					current.GetType().Name.Contains("ScrollViewer") ||
					current.GetType().Name.Contains("ItemsControl"))
				{
					return;
				}
				current = current.GetVisualParent();
			}

			_draggedPanel = panel;
			_isDraggingPanel = true;
			_panelStartPointerPosition = e.GetPosition(this);
			
			_panelStartLeft = Canvas.GetLeft(panel);
			_panelStartTop = Canvas.GetTop(panel);

			if (double.IsNaN(_panelStartLeft))
				_panelStartLeft = 0;
			if (double.IsNaN(_panelStartTop))
				_panelStartTop = 0;

			if (header != null)
			{
				e.Pointer.Capture(header);
			}
			e.Handled = true;
		}

		private void OnPanelHeaderPointerMoved(object sender, PointerEventArgs e)
		{
			if (_isDraggingPanel && _draggedPanel != null)
			{
				var currentPos = e.GetPosition(this);
				double dx = currentPos.X - _panelStartPointerPosition.X;
				double dy = currentPos.Y - _panelStartPointerPosition.Y;

				Canvas.SetLeft(_draggedPanel, _panelStartLeft + dx);
				Canvas.SetTop(_draggedPanel, _panelStartTop + dy);
				e.Handled = true;
			}
		}

		private void OnPanelHeaderPointerReleased(object sender, PointerReleasedEventArgs e)
		{
			if (_isDraggingPanel)
			{
				e.Pointer.Capture(null);
				_isDraggingPanel = false;
				_draggedPanel = null;
				e.Handled = true;
			}
		}

		// ── Layer drag-to-reorder ────────────────────────────────────────────────
		private bool _isDraggingLayer = false;
		private LayerViewModel? _draggedLayerVM = null;

		private void OnLayerDragHandlePressed(object? sender, PointerPressedEventArgs e)
		{
			var vm = DataContext as PaintViewModel;
			if (vm == null || sender is not Control ctrl) return;
			var layerVM = ctrl.DataContext as LayerViewModel;
			if (layerVM == null) return;

			var listBox = this.FindControl<ListBox>("LayersListBox");
			if (listBox == null) return;

			vm.SaveHistoryState();

			_isDraggingLayer = true;
			_draggedLayerVM = layerVM;
			_draggedLayerVM.IsDragging = true;
			e.Pointer.Capture(listBox);
			e.Handled = true;
		}

		private void OnLayerDragHandleMoved(object? sender, PointerEventArgs e)
		{
			if (!_isDraggingLayer || _draggedLayerVM == null) return;
			e.Handled = true;

			var listBox = this.FindControl<ListBox>("LayersListBox");
			if (listBox == null) return;

			var props = e.GetCurrentPoint(listBox).Properties;
			if (!props.IsLeftButtonPressed)
			{
				_isDraggingLayer = false;
				e.Pointer.Capture(null);
				_draggedLayerVM.IsDragging = false;
				_draggedLayerVM = null;
				return;
			}

			var vm = DataContext as PaintViewModel;
			if (vm != null)
			{
				int currentIndex = vm.Layers.IndexOf(_draggedLayerVM);
				if (currentIndex >= 0)
				{
					var pos = e.GetPosition(listBox);
					int toIndex = GetLayerDropIndex(listBox, pos, vm.Layers.Count);
					if (toIndex >= 0 && toIndex != currentIndex)
					{
						var current = vm.ActiveLayer;
						vm.Layers.Move(currentIndex, toIndex);
						if (current != null)
						{
							vm.ActiveLayer = current;
						}
						vm.UpdateCanvasPreview();
					}
				}
			}
		}

		private void OnLayerDragHandleReleased(object? sender, PointerReleasedEventArgs e)
		{
			if (!_isDraggingLayer) return;

			_isDraggingLayer = false;
			var listBox = this.FindControl<ListBox>("LayersListBox");
			if (listBox != null)
			{
				e.Pointer.Capture(null);
			}

			if (_draggedLayerVM != null)
			{
				_draggedLayerVM.IsDragging = false;
				_draggedLayerVM = null;
			}
			e.Handled = true;
		}

		private static int GetLayerDropIndex(ListBox listBox, Point posInListBox, int totalLayers)
		{
			for (int i = 0; i < totalLayers; i++)
			{
				var container = listBox.ContainerFromIndex(i) as Control;
				if (container == null) continue;
				var topLeft = container.TranslatePoint(new Point(0, 0), listBox);
				if (topLeft == null) continue;
				var rect = new Rect(topLeft.Value, container.Bounds.Size);
				if (rect.Contains(posInListBox))
					return i;
			}
			return -1;
		}

		// ── Color drag-to-reorder ────────────────────────────────────────────────
		private bool _isDraggingColor = false;
		private Color? _draggedColor = null;
		private int _draggedColorIndex = -1;

		private void OnPaletteColorPointerPressed(object? sender, PointerPressedEventArgs e)
		{
			var vm = DataContext as PaintViewModel;
			if (vm == null || vm.SelectedPalette == null) return;
			if (sender is not Border border || border.DataContext is not Color color) return;

			// Set active color immediately on press
			vm.ActiveColor = color;

			if (!vm.SelectedPalette.IsModifiable) return;

			var itemsControl = this.FindControl<ItemsControl>("PaletteColorsItemsControl");
			if (itemsControl == null) return;

			int index = -1;
			for (int i = 0; i < vm.PaletteColors.Count; i++)
			{
				var container = itemsControl.ContainerFromIndex(i);
				if (container != null && (container == border || container == border.Parent || container == border.Parent?.Parent))
				{
					index = i;
					break;
				}
			}

			if (index >= 0)
			{
				_isDraggingColor = true;
				_draggedColor = color;
				_draggedColorIndex = index;
				vm.DraggedColor = color;

				e.Pointer.Capture(itemsControl);
				e.Handled = true;
			}
		}

		private void OnPaletteColorPointerMoved(object? sender, PointerEventArgs e)
		{
			if (!_isDraggingColor || _draggedColorIndex < 0 || _draggedColor == null) return;

			var itemsControl = this.FindControl<ItemsControl>("PaletteColorsItemsControl");
			if (itemsControl == null) return;

			var props = e.GetCurrentPoint(itemsControl).Properties;
			if (!props.IsLeftButtonPressed)
			{
				e.Pointer.Capture(null);
				CleanupColorDrag();
				return;
			}

			var vm = DataContext as PaintViewModel;
			if (vm == null || vm.SelectedPalette == null) return;

			var pos = e.GetPosition(itemsControl);
			int toIndex = GetColorDropIndex(itemsControl, pos, vm.PaletteColors.Count);
			if (toIndex >= 0 && toIndex < vm.PaletteColors.Count && _draggedColorIndex >= 0 && _draggedColorIndex < vm.PaletteColors.Count && toIndex != _draggedColorIndex)
			{
				vm.PaletteColors.Move(_draggedColorIndex, toIndex);

				if (_draggedColorIndex < vm.SelectedPalette.Colors.Count && toIndex < vm.SelectedPalette.Colors.Count)
				{
					var color = vm.SelectedPalette.Colors[_draggedColorIndex];
					vm.SelectedPalette.Colors.RemoveAt(_draggedColorIndex);
					vm.SelectedPalette.Colors.Insert(toIndex, color);
				}
				_draggedColorIndex = toIndex;
				vm.SavePalettes();
			}
			e.Handled = true;
		}

		private void OnPaletteColorPointerReleased(object? sender, PointerReleasedEventArgs e)
		{
			if (_isDraggingColor)
			{
				e.Pointer.Capture(null);
				CleanupColorDrag();
				e.Handled = true;
			}
		}

		private void CleanupColorDrag()
		{
			var vm = DataContext as PaintViewModel;
			if (vm != null)
			{
				vm.DraggedColor = null;
			}
			_isDraggingColor = false;
			_draggedColor = null;
			_draggedColorIndex = -1;
		}

		private static int GetColorDropIndex(ItemsControl itemsControl, Point posInItemsControl, int totalColors)
		{
			if (totalColors == 0) return -1;

			double minDistance = double.MaxValue;
			int closestIndex = -1;

			for (int i = 0; i < totalColors; i++)
			{
				var container = itemsControl.ContainerFromIndex(i) as Control;
				if (container == null) continue;
				var topLeft = container.TranslatePoint(new Point(0, 0), itemsControl);
				if (topLeft == null) continue;

				var center = new Point(topLeft.Value.X + container.Bounds.Width / 2.0, topLeft.Value.Y + container.Bounds.Height / 2.0);
				double dx = posInItemsControl.X - center.X;
				double dy = posInItemsControl.Y - center.Y;
				double distance = dx * dx + dy * dy;

				if (distance < minDistance)
				{
					minDistance = distance;
					closestIndex = i;
				}
			}
			return closestIndex;
		}

		private void OnSetActiveColorClick(object? sender, RoutedEventArgs e)
		{
			var menuItem = sender as MenuItem;
			if (menuItem?.CommandParameter is Border border && border.DataContext is Color color)
			{
				var vm = DataContext as PaintViewModel;
				if (vm != null)
				{
					vm.ActiveColor = color;
				}
			}
		}

		private void OnDuplicateColorClick(object? sender, RoutedEventArgs e)
		{
			var menuItem = sender as MenuItem;
			if (menuItem?.CommandParameter is Border border)
			{
				var vm = DataContext as PaintViewModel;
				var itemsControl = this.FindControl<ItemsControl>("PaletteColorsItemsControl");
				if (vm != null && itemsControl != null)
				{
					int index = -1;
					for (int i = 0; i < vm.PaletteColors.Count; i++)
					{
						var container = itemsControl.ContainerFromIndex(i);
						if (container != null && (container == border || container == border.Parent || container == border.Parent?.Parent))
						{
							index = i;
							break;
						}
					}
					if (index >= 0)
					{
						vm.DuplicateColorAtIndex(index);
					}
				}
			}
		}

		private void OnRemoveColorClick(object? sender, RoutedEventArgs e)
		{
			var menuItem = sender as MenuItem;
			if (menuItem?.CommandParameter is Border border)
			{
				var vm = DataContext as PaintViewModel;
				var itemsControl = this.FindControl<ItemsControl>("PaletteColorsItemsControl");
				if (vm != null && itemsControl != null)
				{
					int index = -1;
					for (int i = 0; i < vm.PaletteColors.Count; i++)
					{
						var container = itemsControl.ContainerFromIndex(i);
						if (container != null && (container == border || container == border.Parent || container == border.Parent?.Parent))
						{
							index = i;
							break;
						}
					}
					if (index >= 0)
					{
						vm.DeleteColorAtIndex(index);
					}
				}
			}
		}

		private void OnWorkspaceCanvasSizeChanged(object? sender, SizeChangedEventArgs e)
		{
			var canvas = sender as Canvas;
			if (canvas == null || e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
				return;

			var layersPanel = this.FindControl<Border>("LayersPanel");
			if (layersPanel != null)
			{
				layersPanel.Height = Math.Max(150, e.NewSize.Height - 50);
			}

			var palettePanel = this.FindControl<Border>("PalettePanel");
			if (palettePanel != null && !_palettePositionInitialized)
			{
				double left = e.NewSize.Width - palettePanel.Width - 50;
				double top = (e.NewSize.Height - palettePanel.Height) / 2;

				Canvas.SetLeft(palettePanel, Math.Max(0, left));
				Canvas.SetTop(palettePanel, Math.Max(0, top));
				_palettePositionInitialized = true;
			}
		}

		private void OnGradientButtonContextRequested(object? sender, ContextRequestedEventArgs e)
		{
			var button = sender as Button;
			if (button != null)
			{
				var flyout = Avalonia.Controls.Primitives.FlyoutBase.GetAttachedFlyout(button) as Flyout;
				if (flyout?.Content is StyledElement content)
				{
					content.DataContext = button.DataContext;
				}
				Avalonia.Controls.Primitives.FlyoutBase.ShowAttachedFlyout(button);
				e.Handled = true;
			}
		}

		private void OnCloseFlyoutClick(object? sender, RoutedEventArgs e)
		{
			var control = sender as Control;
			if (control != null)
			{
				var vm = DataContext as PaintViewModel;
				if (vm != null)
				{
					if (vm.ApplyStopsInputCommand.CanExecute(null))
					{
						vm.ApplyStopsInputCommand.Execute(null);
					}
				}

				var presenter = control.FindAncestorOfType<FlyoutPresenter>();
				if (presenter?.Parent is Avalonia.Controls.Primitives.Popup popup)
				{
					popup.IsOpen = false;
				}
			}
		}

		private void OnLayerNameDoubleTapped(object? sender, TappedEventArgs e)
		{
			var textBlock = sender as TextBlock;
			if (textBlock?.DataContext is LayerViewModel layerVM)
			{
				layerVM.IsEditingName = true;
				var panel = textBlock.Parent as Panel;
				if (panel != null)
				{
					foreach (var child in panel.Children)
					{
						if (child is TextBox textBox)
						{
							Avalonia.Threading.Dispatcher.UIThread.Post(() =>
							{
								textBox.Focus();
								textBox.SelectAll();
							});
							break;
						}
					}
				}
			}
		}

		private void OnLayerNameEditLostFocus(object? sender, RoutedEventArgs e)
		{
			var textBox = sender as TextBox;
			if (textBox?.DataContext is LayerViewModel layerVM)
			{
				layerVM.IsEditingName = false;
			}
		}

		private void OnLayerNameEditKeyDown(object? sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter)
			{
				var textBox = sender as TextBox;
				if (textBox?.DataContext is LayerViewModel layerVM)
				{
					layerVM.IsEditingName = false;
				}
				e.Handled = true;
			}
			else if (e.Key == Key.Escape)
			{
				var textBox = sender as TextBox;
				if (textBox?.DataContext is LayerViewModel layerVM)
				{
					layerVM.IsEditingName = false;
				}
				e.Handled = true;
			}
		}
	}

	public class ColorToBrushConverter : IMultiValueConverter
	{
		public static readonly ColorToBrushConverter Instance = new();

		public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
		{
			if (values.Count >= 2 && values[0] is Color itemColor && values[1] is Color activeColor)
			{
				if (itemColor == activeColor)
				{
					return new SolidColorBrush(Color.Parse("#FFD700")); // Gold
				}
			}
			return new SolidColorBrush(Color.Parse("#444444")); // Default border brush
		}
	}

	public class ColorToThicknessConverter : IMultiValueConverter
	{
		public static readonly ColorToThicknessConverter Instance = new();

		public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
		{
			if (values.Count >= 2 && values[0] is Color itemColor && values[1] is Color activeColor)
			{
				if (itemColor == activeColor)
				{
					return new Thickness(2);
				}
			}
			return new Thickness(1);
		}
	}

	public class ColorToOpacityConverter : IMultiValueConverter
	{
		public static readonly ColorToOpacityConverter Instance = new();

		public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
		{
			if (values.Count >= 2 && values[0] is Color itemColor && values[1] is Color draggedColor)
			{
				if (itemColor == draggedColor)
				{
					return 0.5;
				}
			}
			return 1.0;
		}
	}
}
