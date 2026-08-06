using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;
using NyxAssetsEditor.ViewModels.Common;

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
			_ = new FloatingPanelInteraction(this, titleBar, minWidth: 400, minHeight: 300);
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
			FileTypeChoices = FilePickerFilters.ForArchiveExtension(extension)
		});

		return file?.Path.LocalPath;
	}
}
