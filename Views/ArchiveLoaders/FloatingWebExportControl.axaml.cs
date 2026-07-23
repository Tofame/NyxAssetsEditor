using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;

namespace NyxAssetsEditor.Views.ArchiveLoaders;

public partial class FloatingWebExportControl : UserControl
{
	public FloatingWebExportControl()
	{
		InitializeComponent();
		
		var titleBar = this.FindControl<Border>("TitleBar");
		if (titleBar != null)
		{
			_ = new FloatingPanelInteraction(this, titleBar, null, minWidth: 300, minHeight: 350);
		}
	}

	private async void OnBrowseFolderClick(object? sender, RoutedEventArgs e)
	{
		if (DataContext is not FloatingWebExportViewModel viewModel) return;

		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null) return;

		var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
		{
			Title = "Select Web Export Directory",
			AllowMultiple = false
		});

		if (folders != null && folders.Count > 0)
		{
			viewModel.ExportPath = folders[0].Path.LocalPath;
		}
	}
}
