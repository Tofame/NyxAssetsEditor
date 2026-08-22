using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using NyxAssets.Sprites;
using NyxAssets.Things;
using NyxAssets.Things.Exchange;
using NyxAssetsEditor.Services.Exchange;
using NyxAssetsEditor.Services.ImportExport;
using NyxAssetsEditor.Services.Rendering;
using NyxAssetsEditor.ViewModels.Common;
using NyxAssetsEditor.ViewModels.Core;
using NyxAssetsEditor.ViewModels.Pages;

namespace NyxAssetsEditor.Views.Pages;

public enum AssetImportKind
{
	Sprites,
	Things,
}

public sealed class AssetImportPreviewItem : ViewModelBase
{
	private readonly AssetImportKind _kind;
	private readonly ClientDataReadOptions? _thingOptions;
	private readonly SpriteRenderer _renderer;
	private IImage? _preview;
	private bool _previewRequested;
	private string _details;

	public AssetImportPreviewItem(string path, AssetImportKind kind, ClientDataReadOptions? thingOptions, SpriteRenderer renderer)
	{
		Path = path;
		FileName = System.IO.Path.GetFileName(path);
		_kind = kind;
		_thingOptions = thingOptions;
		_renderer = renderer;
		_details = System.IO.Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
	}

	public string Path { get; }
	public string FileName { get; }
	public string Details => _details;

	public IImage? Preview
	{
		get
		{
			if (!_previewRequested)
				LoadPreview();
			return _preview;
		}
	}

	private void LoadPreview()
	{
		_previewRequested = true;
		try
		{
			if (_kind == AssetImportKind.Sprites)
			{
				var bitmap = new Bitmap(Path);
				_preview = bitmap;
				_details = $"{bitmap.PixelSize.Width}×{bitmap.PixelSize.Height} {_details}";
				OnPropertyChanged(nameof(Details));
				return;
			}

			if (_thingOptions == null)
				return;
			var document = ThingExchangeHelper.LoadFromPath(Path, _thingOptions);
			_details = $"{document.Thing.Kind} #{document.Thing.Id}";
			OnPropertyChanged(nameof(Details));
			var composed = ThingPreviewRenderer.RenderPreview(document.Thing, document.SpritesRgba);
			if (composed != null)
				_preview = _renderer.ConvertRgba(composed.Width, composed.Height, composed.Pixels);
		}
		catch (Exception ex)
		{
			_details = ex.Message;
			OnPropertyChanged(nameof(Details));
		}
	}

	public void DisposePreview()
	{
		(_preview as IDisposable)?.Dispose();
		_preview = null;
		_previewRequested = false;
	}
}

public partial class AssetImportDialog : Window
{
	private static readonly Regex DigitPad = new(@"\d+", RegexOptions.Compiled);
	private readonly AssetImportKind _kind;
	private readonly ClientDataReadOptions? _thingOptions;
	private readonly SpriteRenderer _renderer = new();

	public bool IsConfirmed { get; private set; }
	public IReadOnlyList<string> SelectedPaths { get; private set; } = Array.Empty<string>();
	public ObservableCollection<AssetImportPreviewItem> PreviewItems { get; } = new();

	public AssetImportDialog()
	{
		InitializeComponent();
	}

	public AssetImportDialog(AssetImportKind kind, ClientDataReadOptions? thingOptions = null) : this()
	{
		_kind = kind;
		_thingOptions = thingOptions;
		if (TitleText != null)
			TitleText.Text = kind == AssetImportKind.Sprites ? "Import Sprites" : "Import Things";
		if (PreviewList != null)
			PreviewList.ItemsSource = PreviewItems;

		var start = SettingsViewModel.LastAssetImportDirectory;
		if (string.IsNullOrWhiteSpace(start) || !Directory.Exists(start))
			start = SettingsViewModel.LastAssetExportDirectory;
		if (!string.IsNullOrWhiteSpace(start) && Directory.Exists(start) && PathInput != null)
			PathInput.Text = start;

		if (PathInput != null)
		{
			PathInput.LostFocus += (_, _) => ScanFolder(PathInput.Text);
			PathInput.KeyDown += (_, e) =>
			{
				if (e.Key == Avalonia.Input.Key.Enter)
					ScanFolder(PathInput.Text);
			};
		}

		ScanFolder(PathInput?.Text);
		UpdateImportEnabled();
	}

	private async void OnBrowseClick(object? sender, RoutedEventArgs e)
	{
		IStorageFolder? start = null;
		if (Directory.Exists(PathInput?.Text))
			start = await StorageProvider.TryGetFolderFromPathAsync(PathInput!.Text);

		var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
		{
			Title = "Import folder",
			AllowMultiple = false,
			SuggestedStartLocation = start
		});
		if (folders.Count == 0)
			return;

		var path = folders[0].Path.LocalPath;
		if (PathInput != null)
			PathInput.Text = path;
		SettingsViewModel.RememberAssetImportDirectory(path);
		ScanFolder(path);
	}

	private void ScanFolder(string? folder)
	{
		foreach (var item in PreviewItems)
			item.DisposePreview();
		PreviewItems.Clear();

		if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
		{
			SetStatus("Select a folder to preview files.");
			UpdateImportEnabled();
			return;
		}

		IEnumerable<string> files;
		try
		{
			files = Directory.EnumerateFiles(folder);
		}
		catch (Exception ex)
		{
			SetStatus(ex.Message);
			UpdateImportEnabled();
			return;
		}

		var matches = files
			.Where(path => _kind == AssetImportKind.Sprites
				? SupportedFileFormats.IsSupportedImagePath(path)
				: SupportedFileFormats.IsThingExchangePath(path))
			.OrderBy(path => DigitPad.Replace(System.IO.Path.GetFileName(path), match => match.Value.PadLeft(10, '0')), StringComparer.OrdinalIgnoreCase)
			.ToList();

		foreach (var path in matches)
			PreviewItems.Add(new AssetImportPreviewItem(path, _kind, _thingOptions, _renderer));

		SetStatus(matches.Count == 0
			? (_kind == AssetImportKind.Sprites
				? "No PNG/BMP/JPG/WebP files in this folder."
				: "No JSON/OBD files in this folder.")
			: $"{matches.Count} file(s)");
		UpdateImportEnabled();
	}

	private void SetStatus(string text)
	{
		if (StatusText != null)
			StatusText.Text = text;
		if (PreviewLabel != null)
			PreviewLabel.Text = string.IsNullOrEmpty(text) ? "Preview" : $"Preview — {text}";
	}

	private void UpdateImportEnabled()
	{
		if (ImportButton != null)
			ImportButton.IsEnabled = PreviewItems.Count > 0;
	}

	private void OnImportClick(object? sender, RoutedEventArgs e)
	{
		if (PreviewItems.Count == 0)
			return;
		IsConfirmed = true;
		SelectedPaths = PreviewItems.Select(item => item.Path).ToList();
		if (!string.IsNullOrWhiteSpace(PathInput?.Text) && Directory.Exists(PathInput.Text))
			SettingsViewModel.RememberAssetImportDirectory(PathInput.Text);
		Close();
	}

	private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
