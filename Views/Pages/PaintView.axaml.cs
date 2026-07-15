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
			if (e.Source is Image)
			{
				_isDrawing = true;
				HandlePointer(e);
			}
			e.Handled = true;
		}

		private void OnCanvasPointerMoved(object sender, PointerEventArgs e)
		{
			UpdateHoverPosition(e);
			if (_isDrawing)
			{
				HandlePointer(e);
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
				vm.HandleCanvasClick(x, y, isRightClick);
			}
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

		private void OnSetActiveColorClick(object? sender, RoutedEventArgs e)
		{
			var menuItem = sender as MenuItem;
			if (menuItem?.DataContext is Color color)
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
			if (menuItem?.DataContext is Color color)
			{
				var vm = DataContext as PaintViewModel;
				if (vm != null)
				{
					if (vm.DuplicateColorCommand.CanExecute(color))
					{
						vm.DuplicateColorCommand.Execute(color);
					}
				}
			}
		}

		private void OnRemoveColorClick(object? sender, RoutedEventArgs e)
		{
			var menuItem = sender as MenuItem;
			if (menuItem?.DataContext is Color color)
			{
				var vm = DataContext as PaintViewModel;
				if (vm != null)
				{
					if (vm.DeleteColorCommand.CanExecute(color))
					{
						vm.DeleteColorCommand.Execute(color);
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
}
