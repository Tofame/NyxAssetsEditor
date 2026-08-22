using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NyxAssetsEditor.ViewModels.Pages;

namespace NyxAssetsEditor.Views.Pages;

public partial class AssetExportDialog : Window
{
	private static string _lastExportDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
	private static string _lastThingsFormat = "png";
	private static string _lastSpritesFormat = "png";
	private static bool _formatsSeeded;
	private readonly bool _showThingsFormats;

	public bool IsConfirmed { get; private set; }
	public string ExportPath => PathInput?.Text?.Trim() ?? string.Empty;
	public string ExportName => NameInput?.Text?.Trim() ?? "item";
	public bool SkipWestDirection => SkipWestCheckBox?.IsChecked == true && SkipWestCheckBox.IsVisible;
	public string ExportFormat
	{
		get
		{
			if (PngRadio?.IsChecked == true) return "png";
			if (BmpRadio?.IsChecked == true) return "bmp";
			if (JpgRadio?.IsChecked == true) return "jpg";
			if (ObdRadio?.IsChecked == true) return "obd";
			if (NyxRadio?.IsChecked == true) return "nyx-thing";
			return "png";
		}
	}

	public AssetExportDialog()
	{
		InitializeComponent();
	}

	public AssetExportDialog(string defaultName, bool showThingsFormats) : this()
	{
		_showThingsFormats = showThingsFormats;
		NameInput.Text = defaultName;
		PathInput.Text = string.IsNullOrWhiteSpace(SettingsViewModel.LastAssetExportDirectory)
			? _lastExportDirectory
			: SettingsViewModel.LastAssetExportDirectory;
		if (Directory.Exists(PathInput.Text))
			_lastExportDirectory = PathInput.Text;

		if (!showThingsFormats)
		{
			ObdRadio.IsVisible = false;
			NyxRadio.IsVisible = false;
		}

		SeedFormatsFromSettings();
		SubscribeFormatChanges();
		ApplyRememberedFormat(CurrentRememberedFormat);
		if (SkipWestCheckBox != null)
		{
			SkipWestCheckBox.IsChecked = showThingsFormats && SettingsViewModel.LastThingExportSkipWest;
			SkipWestCheckBox.IsCheckedChanged += (_, _) => RememberCurrentChoices(ExportFormat);
		}

		PathInput.TextChanged += (_, _) => UpdateExportEnabled();
		UpdateExportEnabled();
		UpdateSkipWestVisibility();
	}

	private string CurrentRememberedFormat => SettingsViewModel.NormalizeAssetExportFormat(
		_showThingsFormats ? _lastThingsFormat : _lastSpritesFormat, _showThingsFormats);

	private static void SeedFormatsFromSettings()
	{
		if (_formatsSeeded)
			return;
		_lastThingsFormat = SettingsViewModel.NormalizeAssetExportFormat(SettingsViewModel.LastAssetExportFormat, true);
		_lastSpritesFormat = SettingsViewModel.NormalizeAssetExportFormat(SettingsViewModel.LastAssetExportFormat, false);
		_formatsSeeded = true;
	}

	private void ApplyRememberedFormat(string format)
	{
		var radio = format switch
		{
			"bmp" => BmpRadio,
			"jpg" => JpgRadio,
			"obd" => ObdRadio,
			"nyx-thing" => NyxRadio,
			_ => PngRadio
		};
		if (radio != null)
			radio.IsChecked = true;
	}

	private void SubscribeFormatChanges()
	{
		HookFormatRadio(PngRadio);
		HookFormatRadio(BmpRadio);
		HookFormatRadio(JpgRadio);
		HookFormatRadio(ObdRadio);
		HookFormatRadio(NyxRadio);
	}

	private void HookFormatRadio(RadioButton? radio)
	{
		if (radio == null)
			return;
		radio.IsCheckedChanged += OnFormatChanged;
		radio.Click += OnFormatClicked;
	}

	private void OnFormatClicked(object? sender, RoutedEventArgs e) => RememberFromRadio(sender);

	private void OnFormatChanged(object? sender, RoutedEventArgs e)
	{
		UpdateSkipWestVisibility();
		RememberFromRadio(sender);
	}

	private void RememberFromRadio(object? sender)
	{
		if (sender is not RadioButton radio || radio.IsChecked != true)
			return;
		var format = radio == BmpRadio ? "bmp"
			: radio == JpgRadio ? "jpg"
			: radio == ObdRadio ? "obd"
			: radio == NyxRadio ? "nyx-thing"
			: "png";
		RememberCurrentChoices(format);
	}

	private void RememberCurrentChoices(string format)
	{
		format = SettingsViewModel.NormalizeAssetExportFormat(format, _showThingsFormats);
		if (_showThingsFormats)
			_lastThingsFormat = format;
		else
			_lastSpritesFormat = format;
		SettingsViewModel.RememberAssetExport(format, ExportPath, SkipWestDirection, _showThingsFormats);
	}

	private void UpdateSkipWestVisibility()
	{
		if (SkipWestCheckBox == null)
			return;

		bool isGraphicalFormat = PngRadio?.IsChecked == true || BmpRadio?.IsChecked == true || JpgRadio?.IsChecked == true;
		SkipWestCheckBox.IsVisible = _showThingsFormats && isGraphicalFormat;
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
			_lastExportDirectory = folders[0].Path.LocalPath;
			UpdateExportEnabled();
		}
	}

	private void OnExportClick(object? sender, RoutedEventArgs e)
	{
		if (string.IsNullOrWhiteSpace(ExportPath))
			return;

		IsConfirmed = true;
		_lastExportDirectory = ExportPath;
		RememberCurrentChoices(ExportFormat);
		Close();
	}

	private void OnCancelClick(object? sender, RoutedEventArgs e)
	{
		Close();
	}
}
