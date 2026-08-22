using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;

namespace NyxAssetsEditor.Views.ArchiveLoaders;

public partial class FloatingReplacerControl : UserControl
{
	private FloatingReplacerViewModel? _viewModel;
	private bool _autoScrollActive;
	private Point _autoScrollAnchor;
	private DispatcherTimer? _autoScrollTimer;
	private double _autoScrollDeltaY;
	private DateTime _lastPageChangeTime = DateTime.MinValue;
	private int _lastPreviewPage = 1;

	public FloatingReplacerControl()
	{
		InitializeComponent();
		DataContextChanged += OnDataContextChanged;
		HookIdInput(this.FindControl<NumericUpDown>("FromIdInput"), fromField: true);
		HookIdInput(this.FindControl<NumericUpDown>("ToIdInput"), fromField: false);
		var titleBar = this.FindControl<Border>("TitleBar");
		if (titleBar == null) return;

		var interaction = new FloatingPanelInteraction(this, titleBar, null, minWidth: 720, minHeight: 360);
		Register(interaction, "ResizeLeft", 4);
		Register(interaction, "ResizeRight", 1);
		Register(interaction, "ResizeBottom", 2);
		Register(interaction, "ResizeCorner", 3);
		Register(interaction, "ResizeBottomLeft", 5);
		Register(interaction, "ResizeTop", 6);
		Register(interaction, "ResizeTopRight", 7);
		Register(interaction, "ResizeTopLeft", 8);

		PointerMoved += OnGlobalPointerMoved;
		PointerReleased += OnGlobalPointerReleased;
		PointerPressed += OnGlobalPointerPressed;
	}

	private void Register(FloatingPanelInteraction interaction, string name, int direction)
	{
		var handle = this.FindControl<Border>(name);
		if (handle != null)
			interaction.RegisterResizeHandle(handle, direction);
	}

	private void HookIdInput(NumericUpDown? box, bool fromField)
	{
		if (box == null)
			return;
		box.LostFocus += (_, _) => _viewModel?.CommitIdField(fromField);
		box.KeyDown += OnIdKeyDown;
	}

	private static void OnIdKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key is Key.OemMinus or Key.Subtract)
			e.Handled = true;
	}

	private void OnDataContextChanged(object? sender, EventArgs e)
	{
		if (_viewModel != null)
			_viewModel.PropertyChanged -= OnViewModelPropertyChanged;
		_viewModel = DataContext as FloatingReplacerViewModel;
		if (_viewModel != null)
		{
			_viewModel.PropertyChanged += OnViewModelPropertyChanged;
			_lastPreviewPage = _viewModel.PreviewCurrentPage;
		}
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(FloatingReplacerViewModel.FromIdShakeNonce))
			_ = ShakeAsync(this.FindControl<NumericUpDown>("FromIdInput"));
		else if (e.PropertyName == nameof(FloatingReplacerViewModel.ToIdShakeNonce))
			_ = ShakeAsync(this.FindControl<NumericUpDown>("ToIdInput"));
		else if (e.PropertyName == nameof(FloatingReplacerViewModel.PreviewCurrentPage))
			ScrollPreviewPageIntoPlace();
	}

	private void ScrollPreviewPageIntoPlace()
	{
		if (_viewModel == null)
			return;

		var newPage = _viewModel.PreviewCurrentPage;
		var isBackwards = newPage < _lastPreviewPage;
		_lastPreviewPage = newPage;

		var listBox = this.FindControl<ListBox>("PreviewListBox");
		if (listBox == null)
			return;

		Dispatcher.UIThread.Post(() =>
		{
			var scrollViewer = listBox.FindDescendantOfType<ScrollViewer>();
			if (scrollViewer == null)
				return;

			scrollViewer.Offset = isBackwards
				? new Vector(scrollViewer.Offset.X, scrollViewer.Extent.Height)
				: new Vector(scrollViewer.Offset.X, 0);
		}, DispatcherPriority.Loaded);
	}

	private void OnPreviewListBoxPointerWheelChanged(object? sender, PointerWheelEventArgs e)
	{
		if (_viewModel == null || sender is not ListBox listBox)
			return;

		var scrollViewer = listBox.FindDescendantOfType<ScrollViewer>();
		if (scrollViewer == null)
			return;

		if (e.Delta.Y > 0)
		{
			if (scrollViewer.Offset.Y <= 0.01 && _viewModel.HasPreviousPreviewPage)
			{
				_viewModel.PreviewCurrentPage--;
				e.Handled = true;
			}
		}
		else if (e.Delta.Y < 0)
		{
			var maxScroll = scrollViewer.Extent.Height - scrollViewer.Viewport.Height;
			if (scrollViewer.Offset.Y >= maxScroll - 0.01 && _viewModel.HasNextPreviewPage)
			{
				_viewModel.PreviewCurrentPage++;
				e.Handled = true;
			}
		}
	}

	private void OnPreviewListBoxPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (sender is not ListBox listBox)
			return;

		var prop = e.GetCurrentPoint(listBox).Properties;
		if (prop.IsMiddleButtonPressed)
		{
			e.Handled = true;
			if (_autoScrollActive)
				StopAutoScroll();
			else
				StartAutoScroll(listBox, e);
		}
		else if (_autoScrollActive)
		{
			StopAutoScroll();
			e.Handled = true;
		}
	}

	private void StartAutoScroll(ListBox listBox, PointerPressedEventArgs e)
	{
		_autoScrollActive = true;
		var mainGrid = this.FindControl<Canvas>("AutoScrollCanvas")?.Parent as Control;
		if (mainGrid == null)
			return;

		_autoScrollAnchor = e.GetPosition(mainGrid);

		var indicator = this.FindControl<Image>("AutoScrollIndicator");
		if (indicator != null)
		{
			Canvas.SetLeft(indicator, _autoScrollAnchor.X - 16);
			Canvas.SetTop(indicator, _autoScrollAnchor.Y - 16);
			indicator.IsVisible = true;
		}

		_autoScrollDeltaY = 0;
		e.Pointer.Capture(listBox);

		_autoScrollTimer?.Stop();
		_autoScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
		_autoScrollTimer.Tick += (_, _) =>
		{
			if (!_autoScrollActive || _viewModel == null)
			{
				_autoScrollTimer?.Stop();
				return;
			}

			var scrollViewer = listBox.FindDescendantOfType<ScrollViewer>();
			if (scrollViewer == null || Math.Abs(_autoScrollDeltaY) <= 10)
				return;

			var speed = (_autoScrollDeltaY - Math.Sign(_autoScrollDeltaY) * 10) * 0.15;
			scrollViewer.Offset = new Vector(scrollViewer.Offset.X, scrollViewer.Offset.Y + speed);

			if (speed < 0 && scrollViewer.Offset.Y <= 0.01)
			{
				if ((DateTime.Now - _lastPageChangeTime).TotalMilliseconds > 800 && _viewModel.HasPreviousPreviewPage)
				{
					_viewModel.PreviewCurrentPage--;
					_lastPageChangeTime = DateTime.Now;
				}
			}
			else if (speed > 0)
			{
				var maxScroll = scrollViewer.Extent.Height - scrollViewer.Viewport.Height;
				if (scrollViewer.Offset.Y >= maxScroll - 0.01
					&& (DateTime.Now - _lastPageChangeTime).TotalMilliseconds > 800
					&& _viewModel.HasNextPreviewPage)
				{
					_viewModel.PreviewCurrentPage++;
					_lastPageChangeTime = DateTime.Now;
				}
			}
		};
		_autoScrollTimer.Start();
	}

	private void StopAutoScroll()
	{
		if (!_autoScrollActive)
			return;

		_autoScrollActive = false;
		_autoScrollTimer?.Stop();
		_autoScrollTimer = null;

		var indicator = this.FindControl<Image>("AutoScrollIndicator");
		if (indicator != null)
			indicator.IsVisible = false;
	}

	private void OnGlobalPointerMoved(object? sender, PointerEventArgs e)
	{
		if (!_autoScrollActive)
			return;

		var mainGrid = this.FindControl<Canvas>("AutoScrollCanvas")?.Parent as Control;
		if (mainGrid != null)
		{
			var currentPos = e.GetPosition(mainGrid);
			_autoScrollDeltaY = currentPos.Y - _autoScrollAnchor.Y;
		}
	}

	private void OnGlobalPointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		if (!_autoScrollActive || e.InitialPressMouseButton != MouseButton.Middle)
			return;

		var mainGrid = this.FindControl<Canvas>("AutoScrollCanvas")?.Parent as Control;
		if (mainGrid == null)
			return;

		var currentPos = e.GetPosition(mainGrid);
		if (Math.Abs(currentPos.Y - _autoScrollAnchor.Y) > 15)
		{
			StopAutoScroll();
			e.Handled = true;
		}
	}

	private void OnGlobalPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (_autoScrollActive)
		{
			StopAutoScroll();
			e.Handled = true;
		}
	}

	private static async Task ShakeAsync(Control? control)
	{
		if (control == null)
			return;
		var transform = control.RenderTransform as TranslateTransform ?? new TranslateTransform();
		control.RenderTransform = transform;
		foreach (var offset in new[] { 0d, -7d, 7d, -5d, 5d, -3d, 3d, 0d })
		{
			transform.X = offset;
			await Task.Delay(35);
		}
	}
}
