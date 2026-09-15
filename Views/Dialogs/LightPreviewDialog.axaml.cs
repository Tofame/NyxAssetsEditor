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

	private void OnApplyClick(object? sender, RoutedEventArgs e)
	{
		if (DataContext is LightPreviewDialogViewModel vm)
			vm.ApplyLightToEditor();
	}

	private void OnResetClick(object? sender, RoutedEventArgs e)
	{
		if (DataContext is LightPreviewDialogViewModel vm)
			vm.ResetLight();
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
		if (DataContext is LightPreviewDialogViewModel vm && sender is Button { Tag: int index })
			vm.LightColor = index;
	}

	private void OnGlobalPaletteClick(object? sender, RoutedEventArgs e)
	{
		if (DataContext is LightPreviewDialogViewModel vm && sender is Button { Tag: int index })
			vm.GlobalColor = index;
	}
}
