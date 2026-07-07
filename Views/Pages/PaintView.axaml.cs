using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using NyxAssetsEditor.ViewModels.Pages;

namespace NyxAssetsEditor.Views.Pages
{
	public partial class PaintView : UserControl
	{
		private bool _isDrawing = false;

		public PaintView()
		{
			InitializeComponent();
		}

		private void OnCanvasPointerPressed(object sender, PointerPressedEventArgs e)
		{
			_isDrawing = true;
			HandlePointer(e);
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
	}
}
