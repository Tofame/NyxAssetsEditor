using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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
		var title = isSprToAssets ? "Open Sprite Archive (.spr)" : "Open Assets Archive (.assets)";
		var ext = isSprToAssets ? "*.spr" : "*.assets";
		var typeName = isSprToAssets ? "SPR Archive" : "Assets Archive";

		var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = title,
			AllowMultiple = false,
			FileTypeFilter = new[]
			{
				new FilePickerFileType(typeName) { Patterns = new[] { ext } }
			}
		});

		if (files != null && files.Count > 0)
		{
			vm.SprSourcePath = files[0].Path.LocalPath;
		}
	}

	private async void OnBrowseSprTarget(object? sender, RoutedEventArgs e)
	{
		var vm = ViewModel;
		if (vm == null) return;

		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null) return;

		var isSprToAssets = vm.SprToAssetsMode;
		var title = isSprToAssets ? "Save Assets Archive (.assets)" : "Save Sprite Archive (.spr)";
		var ext = isSprToAssets ? ".assets" : ".spr";
		var typeName = isSprToAssets ? "Assets Archive" : "SPR Archive";

		var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = title,
			DefaultExtension = ext,
			FileTypeChoices = new[]
			{
				new FilePickerFileType(typeName) { Patterns = new[] { "*" + ext } }
			}
		});

		if (file != null)
		{
			vm.SprTargetPath = file.Path.LocalPath;
		}
	}

	private async void OnBrowseDatSource(object? sender, RoutedEventArgs e)
	{
		var vm = ViewModel;
		if (vm == null) return;

		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null) return;

		var isDatToJson = vm.DatToThingsMode;
		var title = isDatToJson ? "Open Things Dat File (.dat)" : "Open Things JSON File (.json)";
		var ext = isDatToJson ? "*.dat" : "*.json";
		var typeName = isDatToJson ? "DAT File" : "JSON File";

		var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = title,
			AllowMultiple = false,
			FileTypeFilter = new[]
			{
				new FilePickerFileType(typeName) { Patterns = new[] { ext } }
			}
		});

		if (files != null && files.Count > 0)
		{
			vm.DatSourcePath = files[0].Path.LocalPath;
		}
	}

	private async void OnBrowseDatTarget(object? sender, RoutedEventArgs e)
	{
		var vm = ViewModel;
		if (vm == null) return;

		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null) return;

		var isDatToJson = vm.DatToThingsMode;
		var title = isDatToJson ? "Save Things JSON File (.json)" : "Save Things Dat File (.dat)";
		var ext = isDatToJson ? ".json" : ".dat";
		var typeName = isDatToJson ? "JSON File" : "DAT File";

		var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = title,
			DefaultExtension = ext,
			FileTypeChoices = new[]
			{
				new FilePickerFileType(typeName) { Patterns = new[] { "*" + ext } }
			}
		});

		if (file != null)
		{
			vm.DatTargetPath = file.Path.LocalPath;
		}
	}

	private async void OnBrowseMigSourceSpr(object? sender, RoutedEventArgs e)
	{
		var vm = ViewModel;
		if (vm == null) return;

		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null) return;

		var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = "Open Source Sprite Archive (.spr)",
			AllowMultiple = false,
			FileTypeFilter = new[]
			{
				new FilePickerFileType("SPR Archive") { Patterns = new[] { "*.spr" } }
			}
		});

		if (files != null && files.Count > 0)
		{
			vm.MigSourceSprPath = files[0].Path.LocalPath;
		}
	}

	private async void OnBrowseMigSourceDat(object? sender, RoutedEventArgs e)
	{
		var vm = ViewModel;
		if (vm == null) return;

		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null) return;

		var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = "Open Source Things Dat (.dat)",
			AllowMultiple = false,
			FileTypeFilter = new[]
			{
				new FilePickerFileType("DAT File") { Patterns = new[] { "*.dat" } }
			}
		});

		if (files != null && files.Count > 0)
		{
			vm.MigSourceDatPath = files[0].Path.LocalPath;
		}
	}

	private async void OnBrowseMigTargetSpr(object? sender, RoutedEventArgs e)
	{
		var vm = ViewModel;
		if (vm == null) return;

		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null) return;

		var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = "Save Target Sprite Archive (.spr)",
			DefaultExtension = ".spr",
			FileTypeChoices = new[]
			{
				new FilePickerFileType("SPR Archive") { Patterns = new[] { "*.spr" } }
			}
		});

		if (file != null)
		{
			vm.MigTargetSprPath = file.Path.LocalPath;
		}
	}

	private async void OnBrowseMigTargetDat(object? sender, RoutedEventArgs e)
	{
		var vm = ViewModel;
		if (vm == null) return;

		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null) return;

		var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = "Save Target Things Dat (.dat)",
			DefaultExtension = ".dat",
			FileTypeChoices = new[]
			{
				new FilePickerFileType("DAT File") { Patterns = new[] { "*.dat" } }
			}
		});

		if (file != null)
		{
			vm.MigTargetDatPath = file.Path.LocalPath;
		}
	}
}
