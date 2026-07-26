using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace NyxAssetsEditor.Views.Pages;

public partial class SlicerExportDialog : Window
{
	public bool IsConfirmed { get; private set; }
	public string ExportPath => PathInput?.Text?.Trim() ?? string.Empty;
	public bool ExportSelectedOnly => ExportSelectedOnlyCheckBox?.IsVisible == true && ExportSelectedOnlyCheckBox.IsChecked == true;

	public string SelectedFormat
	{
		get
		{
			if (FormatJpg?.IsChecked == true) return "jpg";
			if (FormatBmp?.IsChecked == true) return "bmp";
			return "png";
		}
	}

	public SlicerExportDialog()
	{
		InitializeComponent();
	}

	public SlicerExportDialog(string? initialDirectory, bool hasSelection = false) : this()
	{
		if (!string.IsNullOrWhiteSpace(initialDirectory))
			PathInput.Text = initialDirectory;
		ExportSelectedOnlyCheckBox.IsVisible = hasSelection;
		ExportSelectedOnlyCheckBox.IsChecked = hasSelection;
		PathInput.TextChanged += (_, _) => UpdateExportEnabled();
		UpdateExportEnabled();
	}

	private void UpdateExportEnabled()
	{
		if (ExportButton != null)
			ExportButton.IsEnabled = !string.IsNullOrWhiteSpace(ExportPath);
	}

	private async void OnBrowseClick(object? sender, RoutedEventArgs e)
	{
		IStorageFolder? start = null;
		if (Directory.Exists(ExportPath))
			start = await StorageProvider.TryGetFolderFromPathAsync(ExportPath);

		var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
		{
			Title = "Export folder",
			AllowMultiple = false,
			SuggestedStartLocation = start
		});
		if (folders.Count > 0)
		{
			PathInput.Text = folders[0].Path.LocalPath;
			UpdateExportEnabled();
		}
	}

	private void OnExportClick(object? sender, RoutedEventArgs e)
	{
		if (string.IsNullOrWhiteSpace(ExportPath))
			return;

		IsConfirmed = true;
		Close();
	}

	private void OnCancelClick(object? sender, RoutedEventArgs e)
	{
		IsConfirmed = false;
		Close();
	}
}
