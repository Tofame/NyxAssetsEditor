using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;

namespace NyxAssetsEditor.Views.ArchiveLoaders;

public partial class FloatingCompileControl : UserControl
{
	public FloatingCompileControl()
	{
		InitializeComponent();
		DataContextChanged += OnDataContextChanged;

		var titleBar = this.FindControl<Border>("TitleBar");
		if (titleBar != null)
		{
			var interaction = new FloatingPanelInteraction(this, titleBar, minWidth: 400, minHeight: 300);
			// No extra resize handles needed for this small simple control
		}
	}

	private void OnDataContextChanged(object? sender, EventArgs e)
	{
		if (DataContext is FloatingCompileViewModel vm)
		{
			vm.RequestSavePathHandler = ShowSaveFileDialogAsync;
		}
	}

	private async Task<string?> ShowSaveFileDialogAsync(string suggestedFileName, string extension)
	{
		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null) return null;

		var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = $"Select Output Path for {extension}",
			DefaultExtension = extension,
			SuggestedFileName = suggestedFileName,
			FileTypeChoices = extension switch
			{
				".spr" => new[] { new FilePickerFileType("Nyx Sprite Archive") { Patterns = new[] { "*.spr" } } },
				".assets" => new[] { new FilePickerFileType("Nyx Asset Archive") { Patterns = new[] { "*.assets" } } },
				".dat" => new[] { new FilePickerFileType("Nyx Dat Archive") { Patterns = new[] { "*.dat" } } },
				".json" => new[] { new FilePickerFileType("Nyx Things JSON") { Patterns = new[] { "*.json" } } },
				_ => Array.Empty<FilePickerFileType>()
			}
		});

		return file?.Path.LocalPath;
	}
}
