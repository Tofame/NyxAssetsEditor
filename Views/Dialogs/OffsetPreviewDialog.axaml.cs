using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NyxAssets.Things.Frames;
using NyxAssetsEditor.ViewModels.Dialogs;

namespace NyxAssetsEditor.Views.Dialogs;

public partial class OffsetPreviewDialog : Window
{
	public OffsetPreviewDialog()
	{
		InitializeComponent();
		Closed += OnClosed;
	}

	public OffsetPreviewDialog(OffsetPreviewDialogViewModel viewModel) : this()
	{
		DataContext = viewModel;
	}

	private void OnClosed(object? sender, EventArgs e)
	{
		if (DataContext is OffsetPreviewDialogViewModel vm)
		{
			vm.StopTimer();
		}
	}

	private void OnNorthClick(object? sender, RoutedEventArgs e)
	{
		if (DataContext is OffsetPreviewDialogViewModel vm)
			vm.SetDirection(Direction4.North);
	}

	private void OnEastClick(object? sender, RoutedEventArgs e)
	{
		if (DataContext is OffsetPreviewDialogViewModel vm)
			vm.SetDirection(Direction4.East);
	}

	private void OnSouthClick(object? sender, RoutedEventArgs e)
	{
		if (DataContext is OffsetPreviewDialogViewModel vm)
			vm.SetDirection(Direction4.South);
	}

	private void OnWestClick(object? sender, RoutedEventArgs e)
	{
		if (DataContext is OffsetPreviewDialogViewModel vm)
			vm.SetDirection(Direction4.West);
	}

	private void OnApplyClick(object? sender, RoutedEventArgs e)
	{
		if (DataContext is OffsetPreviewDialogViewModel vm)
			vm.ApplyOffsetToEditor();
	}

	private void OnResetClick(object? sender, RoutedEventArgs e)
	{
		if (DataContext is OffsetPreviewDialogViewModel vm)
			vm.ResetOffsetToEditor();
	}

	private void OnCloseClick(object? sender, RoutedEventArgs e)
	{
		Close();
	}
}
