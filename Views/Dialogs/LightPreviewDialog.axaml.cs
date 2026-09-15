using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NyxAssetsEditor.ViewModels.Dialogs;

namespace NyxAssetsEditor.Views.Dialogs;

public partial class LightPreviewDialog : Window
{
	public LightPreviewDialog()
	{
		InitializeComponent();
		Closed += OnClosed;
	}

	public LightPreviewDialog(LightPreviewDialogViewModel viewModel) : this()
	{
		DataContext = viewModel;
	}

	private void OnClosed(object? sender, EventArgs e)
	{
		if (DataContext is LightPreviewDialogViewModel vm)
		{
			vm.StopTimer();
		}
	}

	private void OnCloseClick(object? sender, RoutedEventArgs e)
	{
		Close();
	}

	private void OnDayClick(object? sender, RoutedEventArgs e)
	{
		if (DataContext is LightPreviewDialogViewModel vm)
			vm.SetDay();
	}

	private void OnNightClick(object? sender, RoutedEventArgs e)
	{
		if (DataContext is LightPreviewDialogViewModel vm)
			vm.SetNight();
	}

	private void OnLightPaletteClick(object? sender, RoutedEventArgs e)
	{
		if (DataContext is LightPreviewDialogViewModel vm && sender is Button btn && btn.Tag != null)
			vm.LightColor = Convert.ToInt32(btn.Tag);
	}

	private void OnGlobalPaletteClick(object? sender, RoutedEventArgs e)
	{
		if (DataContext is LightPreviewDialogViewModel vm && sender is Button btn && btn.Tag != null)
			vm.GlobalColor = Convert.ToInt32(btn.Tag);
	}
}
