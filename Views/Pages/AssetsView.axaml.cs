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
				_viewModel.PositionLooktypeGeneratorHandler = null;
				_viewModel.PositionReplacerHandler = null;
				_viewModel.PositionSlicerHandler = null;
			}

			_viewModel = DataContext as AssetsViewModel;
			if (_viewModel != null)
			{
				_viewModel.CompileAsHandler = ShowCompileAsDialogAsync;
				_viewModel.PositionWebExportHandler = PositionAndOpenWebExport;
				_viewModel.PositionLooktypeGeneratorHandler = PositionAndOpenLooktypeGenerator;
				_viewModel.PositionReplacerHandler = PositionAndOpenReplacer;
				_viewModel.PositionSlicerHandler = PositionAndOpenSlicer;
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

				var spriteExt = spriteFormat == ArchiveFormat.Spr
					? SupportedFileFormats.ExtSpr
					: SupportedFileFormats.ExtAssets;
				var spriteFile = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
				{
					Title = "Compile Sprite Archive As",
					DefaultExtension = spriteExt,
					SuggestedFileName = pair.SpritePanel.FileName,
					FileTypeChoices = FilePickerFilters.ForArchiveExtension(spriteExt)
				});

				if (spriteFile == null)
					return;

				var thingsExt = thingsFormat == ArchiveFormat.Dat
					? SupportedFileFormats.ExtDat
					: SupportedFileFormats.ExtJson;
				var thingsFile = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
				{
					Title = "Compile Things Archive As",
					DefaultExtension = thingsExt,
					SuggestedFileName = pair.ThingsPanel.FileName,
					FileTypeChoices = FilePickerFilters.ForArchiveExtension(thingsExt)
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

		private void PositionAndOpenLooktypeGenerator(double panelW, double panelH)
		{
			if (_viewModel == null) return;

			double posX = 60;
			double posY = 60;

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

			var panel = new NyxAssetsEditor.ViewModels.ArchiveLoaders.FloatingLooktypeGeneratorViewModel(_viewModel)
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

		private void PositionAndOpenReplacer(double panelW, double panelH)
		{
			if (_viewModel == null) return;

			double posX = 60;
			double posY = 60;
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

			_viewModel.AddPanelFromView(new NyxAssetsEditor.ViewModels.ArchiveLoaders.FloatingReplacerViewModel(_viewModel)
			{
				DockState = "Floating",
				PanelWidth = panelW,
				ContentHeight = panelH,
				PositionX = Math.Max(0, posX),
				PositionY = Math.Max(0, posY),
				IsVisible = true,
			});
		}

		private void PositionAndOpenSlicer(double panelW, double panelH, NyxAssetsEditor.ViewModels.ArchiveLoaders.FloatingSpriteLoaderViewModel? origin)
		{
			if (_viewModel == null) return;

			double posX = 60;
			double posY = 60;

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

			var panel = new NyxAssetsEditor.ViewModels.Pages.SpritesheetSlicerViewModel(_viewModel, origin)
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
