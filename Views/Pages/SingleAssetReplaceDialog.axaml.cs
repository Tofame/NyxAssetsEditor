using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace NyxAssetsEditor.Views.Pages;

public partial class SingleAssetReplaceDialog : Window
{
	private readonly IReadOnlyList<FilePickerFileType> _fileTypes;
	private readonly HashSet<string> _extensions;
	private readonly Func<string, string?> _replace;
	private string? _selectedPath;

	public SingleAssetReplaceDialog()
	{
		InitializeComponent();
		_fileTypes = Array.Empty<FilePickerFileType>();
		_extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		_replace = _ => "No replacement handler was configured.";
	}

	public SingleAssetReplaceDialog(
		string heading,
		string instruction,
		IReadOnlyList<FilePickerFileType> fileTypes,
		IEnumerable<string> extensions,
		Func<string, string?> replace) : this()
	{
		Title = heading;
		HeadingText.Text = heading;
		DropInstructionText.Text = instruction;
		SelectedFileText.Text = "Click here to browse, or drop a file";
		_fileTypes = fileTypes;
		_extensions = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
		_replace = replace;
	}

	private async void OnDropZonePressed(object? sender, PointerPressedEventArgs e)
	{
		if (!e.GetCurrentPoint(DropZone).Properties.IsLeftButtonPressed)
			return;
		var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = Title,
			AllowMultiple = false,
			FileTypeFilter = _fileTypes,
		});
		if (files.Count > 0)
			SelectPath(files[0].Path.LocalPath);
	}

	private void OnDragOver(object? sender, DragEventArgs e)
	{
		var path = e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath();
		e.DragEffects = IsSupported(path) ? DragDropEffects.Copy : DragDropEffects.None;
		e.Handled = true;
	}

	private void OnDrop(object? sender, DragEventArgs e)
	{
		var path = e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath();
		if (IsSupported(path))
			SelectPath(path!);
		e.Handled = true;
	}

	private bool IsSupported(string? path) => !string.IsNullOrWhiteSpace(path)
		&& File.Exists(path)
		&& _extensions.Contains(Path.GetExtension(path));

	private void SelectPath(string path)
	{
		if (!IsSupported(path))
		{
			ShowError("Choose a supported replacement file.");
			return;
		}
		_selectedPath = path;
		SelectedFileText.Text = Path.GetFileName(path);
		ConfirmButton.IsEnabled = true;
		ErrorText.IsVisible = false;
	}

	private void OnConfirmClick(object? sender, RoutedEventArgs e)
	{
		if (_selectedPath == null)
			return;
		var error = _replace(_selectedPath);
		if (!string.IsNullOrWhiteSpace(error))
		{
			ShowError(error);
			return;
		}
		Close(true);
	}

	private void ShowError(string error)
	{
		ErrorText.Text = error;
		ErrorText.IsVisible = true;
	}

	private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
