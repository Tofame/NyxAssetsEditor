using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;

namespace NyxAssetsEditor.Views.ArchiveLoaders;

public partial class FloatingLooktypeGeneratorControl : UserControl
{
	private FloatingLooktypeGeneratorViewModel? _viewModel;

	public FloatingLooktypeGeneratorControl()
	{
		InitializeComponent();
		DataContextChanged += OnDataContextChanged;
		var titleBar = this.FindControl<Border>("TitleBar");
		if (titleBar == null) return;
		var interaction = new FloatingPanelInteraction(this, titleBar, 760, 420);
		Register(interaction, "ResizeLeft", 4); Register(interaction, "ResizeRight", 1);
		Register(interaction, "ResizeBottom", 2); Register(interaction, "ResizeCorner", 3);
	}

	private void Register(FloatingPanelInteraction interaction, string name, int direction)
	{
		var border = this.FindControl<Border>(name); if (border != null) interaction.RegisterResizeHandle(border, direction);
	}

	private void OnDataContextChanged(object? sender, EventArgs e)
	{
		if (_viewModel != null) _viewModel.RequestFileDialog -= OnRequestFileDialog;
		_viewModel = DataContext as FloatingLooktypeGeneratorViewModel;
		if (_viewModel != null) _viewModel.RequestFileDialog += OnRequestFileDialog;
	}

	private async void OnRequestFileDialog(object? sender, LooktypeFileRequestEventArgs e)
	{
		if (_viewModel == null || TopLevel.GetTopLevel(this) is not { } topLevel) return;
		var isLua = e.Operation is LooktypeFileOperation.ImportLua or LooktypeFileOperation.ExportLua;
		var type = new FilePickerFileType(isLua ? "Lua looktype" : "XML looktype") { Patterns = new[] { isLua ? "*.lua" : "*.xml" } };
		if (e.Operation is LooktypeFileOperation.ImportLua or LooktypeFileOperation.ImportXml)
		{
			var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import Looktype", AllowMultiple = false, FileTypeFilter = new[] { type } });
			if (files.Count > 0) _viewModel.ImportFile(files[0].Path.LocalPath, e.Operation);
			return;
		}

		var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = "Export All Looktypes", SuggestedFileName = e.SuggestedFileName,
			DefaultExtension = isLua ? ".lua" : ".xml", FileTypeChoices = new[] { type },
		});
		if (file == null) return;
		try { File.WriteAllText(file.Path.LocalPath, _viewModel.GetExportText(e.Operation)); }
		catch (Exception ex) { _viewModel.ReportFileError($"Could not export looktype: {ex.Message}"); }
	}
}
