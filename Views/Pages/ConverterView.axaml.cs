using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NyxAssetsEditor.ViewModels.Common;
using NyxAssetsEditor.ViewModels.Pages;

namespace NyxAssetsEditor.Views.Pages;

public partial class ConverterView : UserControl
{
	public ConverterView()
	{
		InitializeComponent();
	}

	private ConverterViewModel? ViewModel => DataContext as ConverterViewModel;

	private async void OnBrowseSprSource(object? sender, RoutedEventArgs e)
	{
		var vm = ViewModel;
		if (vm == null) return;

		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null) return;

		var isSprToAssets = vm.SprToAssetsMode;
		var ext = isSprToAssets ? SupportedFileFormats.ExtSpr : SupportedFileFormats.ExtAssets;
		var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = isSprToAssets
				? $"Open Sprite Archive ({SupportedFileFormats.ExtSpr})"
				: $"Open Assets Archive ({SupportedFileFormats.ExtAssets})",
			AllowMultiple = false,
			FileTypeFilter = FilePickerFilters.ForArchiveExtension(ext)
		});

		if (files != null && files.Count > 0)
			vm.SprSourcePath = files[0].Path.LocalPath;
	}

	private async void OnBrowseSprTarget(object? sender, RoutedEventArgs e)
	{
		var vm = ViewModel;
		if (vm == null) return;

		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null) return;

		var isSprToAssets = vm.SprToAssetsMode;
		var ext = isSprToAssets ? SupportedFileFormats.ExtAssets : SupportedFileFormats.ExtSpr;
		var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = isSprToAssets
				? $"Save Assets Archive ({SupportedFileFormats.ExtAssets})"
				: $"Save Sprite Archive ({SupportedFileFormats.ExtSpr})",
			DefaultExtension = ext,
			FileTypeChoices = FilePickerFilters.ForArchiveExtension(ext)
		});

		if (file != null)
			vm.SprTargetPath = file.Path.LocalPath;
	}

	private async void OnBrowseDatSource(object? sender, RoutedEventArgs e)
	{
		var vm = ViewModel;
		if (vm == null) return;

		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null) return;

		var isDatToJson = vm.DatToThingsMode;
		var ext = isDatToJson ? SupportedFileFormats.ExtDat : SupportedFileFormats.ExtJson;
		var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = isDatToJson
				? $"Open Things Dat File ({SupportedFileFormats.ExtDat})"
				: $"Open Things JSON File ({SupportedFileFormats.ExtJson})",
			AllowMultiple = false,
			FileTypeFilter = FilePickerFilters.ForArchiveExtension(ext)
		});

		if (files != null && files.Count > 0)
			vm.DatSourcePath = files[0].Path.LocalPath;
	}

	private async void OnBrowseItemsXml(object? sender, RoutedEventArgs e)
	{
		var vm = ViewModel;
		if (vm == null) return;

		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null) return;

		var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = "Open items.xml",
			AllowMultiple = false,
			FileTypeFilter = FilePickerFilters.Only(FilePickerFilters.Xml)
		});

		if (files != null && files.Count > 0)
			vm.ItemsXmlPath = files[0].Path.LocalPath;
	}

	private async void OnBrowseDatTarget(object? sender, RoutedEventArgs e)
	{
		var vm = ViewModel;
		if (vm == null) return;

		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null) return;

		var isDatToJson = vm.DatToThingsMode;
		var ext = isDatToJson ? SupportedFileFormats.ExtJson : SupportedFileFormats.ExtDat;
		var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = isDatToJson
				? $"Save Things JSON File ({SupportedFileFormats.ExtJson})"
				: $"Save Things Dat File ({SupportedFileFormats.ExtDat})",
			DefaultExtension = ext,
			FileTypeChoices = FilePickerFilters.ForArchiveExtension(ext)
		});

		if (file != null)
			vm.DatTargetPath = file.Path.LocalPath;
	}

	private async void OnBrowseMigSourceSpr(object? sender, RoutedEventArgs e)
	{
		var vm = ViewModel;
		if (vm == null) return;

		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null) return;

		var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = $"Open Source Sprite Archive ({SupportedFileFormats.ExtSpr})",
			AllowMultiple = false,
			FileTypeFilter = FilePickerFilters.Only(FilePickerFilters.Spr)
		});

		if (files != null && files.Count > 0)
			vm.MigSourceSprPath = files[0].Path.LocalPath;
	}

	private async void OnBrowseMigSourceDat(object? sender, RoutedEventArgs e)
	{
		var vm = ViewModel;
		if (vm == null) return;

		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null) return;

		var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = $"Open Source Things Dat ({SupportedFileFormats.ExtDat})",
			AllowMultiple = false,
			FileTypeFilter = FilePickerFilters.Only(FilePickerFilters.Dat)
		});

		if (files != null && files.Count > 0)
			vm.MigSourceDatPath = files[0].Path.LocalPath;
	}

	private async void OnBrowseMigTargetSpr(object? sender, RoutedEventArgs e)
	{
		var vm = ViewModel;
		if (vm == null) return;

		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null) return;

		var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = $"Save Target Sprite Archive ({SupportedFileFormats.ExtSpr})",
			DefaultExtension = SupportedFileFormats.ExtSpr,
			FileTypeChoices = FilePickerFilters.Only(FilePickerFilters.Spr)
		});

		if (file != null)
			vm.MigTargetSprPath = file.Path.LocalPath;
	}

	private async void OnBrowseMigTargetDat(object? sender, RoutedEventArgs e)
	{
		var vm = ViewModel;
		if (vm == null) return;

		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null) return;

		var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = $"Save Target Things Dat ({SupportedFileFormats.ExtDat})",
			DefaultExtension = SupportedFileFormats.ExtDat,
			FileTypeChoices = FilePickerFilters.Only(FilePickerFilters.Dat)
		});

		if (file != null)
			vm.MigTargetDatPath = file.Path.LocalPath;
	}
}
