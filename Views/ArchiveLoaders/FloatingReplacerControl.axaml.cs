using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;

namespace NyxAssetsEditor.Views.ArchiveLoaders;

public partial class FloatingReplacerControl : UserControl
{
	private FloatingReplacerViewModel? _viewModel;

	public FloatingReplacerControl()
	{
		InitializeComponent();
		DataContextChanged += OnDataContextChanged;
		HookIdInput(this.FindControl<NumericUpDown>("FromIdInput"), fromField: true);
		HookIdInput(this.FindControl<NumericUpDown>("ToIdInput"), fromField: false);
		var titleBar = this.FindControl<Border>("TitleBar");
		if (titleBar == null) return;

		var interaction = new FloatingPanelInteraction(this, titleBar, null, minWidth: 500, minHeight: 360);
		Register(interaction, "ResizeLeft", 4);
		Register(interaction, "ResizeRight", 1);
		Register(interaction, "ResizeBottom", 2);
		Register(interaction, "ResizeCorner", 3);
		Register(interaction, "ResizeBottomLeft", 5);
		Register(interaction, "ResizeTop", 6);
		Register(interaction, "ResizeTopRight", 7);
		Register(interaction, "ResizeTopLeft", 8);
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
			_viewModel.PropertyChanged += OnViewModelPropertyChanged;
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(FloatingReplacerViewModel.FromIdShakeNonce))
			_ = ShakeAsync(this.FindControl<NumericUpDown>("FromIdInput"));
		else if (e.PropertyName == nameof(FloatingReplacerViewModel.ToIdShakeNonce))
			_ = ShakeAsync(this.FindControl<NumericUpDown>("ToIdInput"));
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
