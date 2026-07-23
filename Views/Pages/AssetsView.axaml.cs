using Avalonia.Controls;
using Avalonia.Platform.Storage;
using NyxAssetsEditor.ViewModels.Common;
using NyxAssetsEditor.ViewModels.Pages;
using System;
using System.Threading.Tasks;

namespace NyxAssetsEditor.Views.Pages
{
	public partial class AssetsView : UserControl
	{
		private AssetsViewModel? _viewModel;

		public AssetsView()
		{
			InitializeComponent();
			DataContextChanged += OnDataContextChanged;
		}

		private void OnDataContextChanged(object? sender, EventArgs e)
		{
			if (_viewModel != null)
			{
				_viewModel.CompileAsHandler = null;
				_viewModel.PositionWebExportHandler = null;
			}

			_viewModel = DataContext as AssetsViewModel;
			if (_viewModel != null)
			{
				_viewModel.CompileAsHandler = ShowCompileAsDialogAsync;
				_viewModel.PositionWebExportHandler = PositionAndOpenWebExport;
			}
		}

		private async Task ShowCompileAsDialogAsync()
		{
			if (_viewModel == null)
				return;

			var topLevel = TopLevel.GetTopLevel(this);
			if (topLevel == null)
				return;

			foreach (var pair in _viewModel.GetCompilePairs())
			{
				var spriteFormat = pair.SpritePanel.ArchiveFormat;
				var thingsFormat = pair.ThingsPanel.ArchiveFormat;

				var spriteFile = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
				{
					Title = "Compile Sprite Archive As",
					DefaultExtension = spriteFormat == ArchiveFormat.Spr ? ".spr" : ".assets",
					SuggestedFileName = pair.SpritePanel.FileName,
					FileTypeChoices = spriteFormat == ArchiveFormat.Spr
						? new[]
						{
							new FilePickerFileType("Nyx Sprite Archive") { Patterns = new[] { "*.spr" } }
						}
						: new[]
						{
							new FilePickerFileType("Nyx Asset Archive") { Patterns = new[] { "*.assets" } }
						}
				});

				if (spriteFile == null)
					return;

				var thingsFile = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
				{
					Title = "Compile Things Archive As",
					DefaultExtension = thingsFormat == ArchiveFormat.Dat ? ".dat" : ".json",
					SuggestedFileName = pair.ThingsPanel.FileName,
					FileTypeChoices = thingsFormat == ArchiveFormat.Dat
						? new[]
						{
							new FilePickerFileType("Nyx Dat Archive") { Patterns = new[] { "*.dat" } }
						}
						: new[]
						{
							new FilePickerFileType("Nyx Things JSON") { Patterns = new[] { "*.json" } }
						}
				});

				if (thingsFile == null)
					return;

				try
				{
					await _viewModel.CompilePairAs(pair, spriteFile.Path.LocalPath, thingsFile.Path.LocalPath);
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"Compile as failed: {ex.Message}");
					pair.SpritePanel.ErrorMessage = $"Compile As failed: {ex.Message}";
					pair.ThingsPanel.ErrorMessage = $"Compile As failed: {ex.Message}";
				}
			}
		}

		private void PositionAndOpenWebExport(double panelW, double panelH)
		{
			if (_viewModel == null) return;

			double posX = 450;
			double posY = 120;

			var centerGrid = this.FindControl<Grid>("CenterDockColumn");
			if (centerGrid != null)
			{
				var bounds = centerGrid.Bounds;
				if (bounds.Width > 0 && bounds.Height > 0)
				{
					posX = bounds.X + (bounds.Width - panelW) / 2;
					posY = bounds.Y + (bounds.Height - panelH) / 2;
				}
			}

			var panel = new NyxAssetsEditor.ViewModels.ArchiveLoaders.FloatingWebExportViewModel(_viewModel)
			{
				DockState = "Floating",
				PanelWidth = panelW,
				ContentHeight = panelH,
				PositionX = Math.Max(0, posX),
				PositionY = Math.Max(0, posY),
				IsVisible = true,
			};

			_viewModel.AddPanelFromView(panel);
		}
	}
}
