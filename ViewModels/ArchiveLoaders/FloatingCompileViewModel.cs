using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NyxAssetsEditor.ViewModels.Core;
using NyxAssetsEditor.ViewModels.Pages;
using NyxAssetsEditor.Services.Things;
using NyxAssetsEditor.Services.Archive;
using NyxAssetsEditor.ViewModels.Common;

namespace NyxAssetsEditor.ViewModels.ArchiveLoaders;

public partial class FloatingCompileViewModel : PanelViewModelBase
{
	public const double DefaultPanelWidth = 600;
	public const double DefaultContentHeight = 350;

	private readonly AssetsViewModel _parent;

	[ObservableProperty]
	private string _title = "Compile Assistant";

	public ObservableCollection<LooktypeArchivePairViewModel> ArchivePairs { get; } = new();

	private LooktypeArchivePairViewModel? _selectedArchivePair;
	public LooktypeArchivePairViewModel? SelectedArchivePair
	{
		get => _selectedArchivePair;
		set
		{
			if (SetProperty(ref _selectedArchivePair, value))
			{
				if (value != null)
				{
					SpriteOutputPath = value.SpritePath;
					ThingsOutputPath = value.ThingsPath;
				}
				else
				{
					SpriteOutputPath = string.Empty;
					ThingsOutputPath = string.Empty;
				}
				OnPropertyChanged(nameof(SpriteOriginalPath));
				OnPropertyChanged(nameof(ThingsOriginalPath));
				OnPropertyChanged(nameof(HasChangesSummary));
			}
		}
	}

	[ObservableProperty]
	private string _spriteOutputPath = string.Empty;

	[ObservableProperty]
	private string _thingsOutputPath = string.Empty;

	[ObservableProperty]
	private string _statusMessage = string.Empty;

	public string SpriteOriginalPath => SelectedArchivePair?.SpritePath ?? string.Empty;
	public string ThingsOriginalPath => SelectedArchivePair?.ThingsPath ?? string.Empty;

	public string HasChangesSummary
	{
		get
		{
			if (SelectedArchivePair == null) return "No archive pair selected.";
			var pair = SelectedArchivePair.Pair;
			var changes = new System.Collections.Generic.List<string>();
			if (pair.ThingsPanel.HasSavedChanges) changes.Add("Things (*)");
			if (pair.SpritePanel.HasSavedChanges) changes.Add("Sprites (*)");
			return changes.Count > 0 ? $"Unsaved: {string.Join(", ", changes)}" : "No unsaved changes.";
		}
	}

	public Func<string, string, Task<string?>>? RequestSavePathHandler { get; set; }

	public FloatingCompileViewModel(AssetsViewModel parent)
	{
		_parent = parent;
		DockState = "Floating";
		PanelWidth = DefaultPanelWidth;
		ContentHeight = DefaultContentHeight;
		RefreshArchivePairs();
	}

	public void RefreshArchivePairs()
	{
		var selected = SelectedArchivePair;
		ArchivePairs.Clear();
		foreach (var pair in _parent.GetCompilePairs())
		{
			ArchivePairs.Add(new LooktypeArchivePairViewModel(pair));
		}
		if (selected != null)
		{
			SelectedArchivePair = ArchivePairs.FirstOrDefault(p =>
				string.Equals(p.SpritePath, selected.SpritePath, StringComparison.OrdinalIgnoreCase) &&
				string.Equals(p.ThingsPath, selected.ThingsPath, StringComparison.OrdinalIgnoreCase))
				?? ArchivePairs.FirstOrDefault();
		}
		else
		{
			SelectedArchivePair = ArchivePairs.FirstOrDefault();
		}
	}

	[RelayCommand]
	private async Task BrowseSprite()
	{
		if (SelectedArchivePair == null || RequestSavePathHandler == null) return;
		var format = SelectedArchivePair.Pair.SpritePanel.ArchiveFormat;
		var ext = format == ArchiveFormat.Spr ? SupportedFileFormats.ExtSpr : SupportedFileFormats.ExtAssets;
		var result = await RequestSavePathHandler(Path.GetFileName(SelectedArchivePair.Pair.SpritePanel.FilePath), ext);
		if (!string.IsNullOrEmpty(result))
		{
			SpriteOutputPath = result;
		}
	}

	[RelayCommand]
	private async Task BrowseThings()
	{
		if (SelectedArchivePair == null || RequestSavePathHandler == null) return;
		var format = SelectedArchivePair.Pair.ThingsPanel.ArchiveFormat;
		var ext = format == ArchiveFormat.Dat ? SupportedFileFormats.ExtDat : SupportedFileFormats.ExtJson;
		var result = await RequestSavePathHandler(Path.GetFileName(SelectedArchivePair.Pair.ThingsPanel.FilePath), ext);
		if (!string.IsNullOrEmpty(result))
		{
			ThingsOutputPath = result;
		}
	}

	[RelayCommand]
	private async Task Compile()
	{
		if (SelectedArchivePair == null) return;
		try
		{
			var pair = SelectedArchivePair.Pair;
			ArchiveCompileService.BackupIfExists(pair.SpritePanel.FilePath);
			ArchiveCompileService.BackupIfExists(pair.ThingsPanel.FilePath);

			ArchiveCompileService.CompilePair(
				pair.SpritePanel,
				pair.ThingsPanel,
				pair.SpritePanel.FilePath,
				pair.ThingsPanel.FilePath);

			await pair.SpritePanel.LoadArchiveAsync(pair.SpritePanel.FilePath, preserveNavigation: true);
			await pair.ThingsPanel.LoadArchiveAsync(pair.ThingsPanel.FilePath, useLastLoadedSprite: false, preserveNavigation: true);

			pair.SpritePanel.HasSavedChanges = false;
			pair.ThingsPanel.HasSavedChanges = false;
			_parent.RefreshCompileCommands();

			OnPropertyChanged(nameof(SpriteOriginalPath));
			OnPropertyChanged(nameof(ThingsOriginalPath));
			OnPropertyChanged(nameof(HasChangesSummary));
			ClosePanel();
		}
		catch (Exception ex)
		{
			StatusMessage = $"Compile failed: {ex.Message}";
		}
	}

	[RelayCommand]
	private async Task CompileAs()
	{
		if (SelectedArchivePair == null) return;
		if (string.IsNullOrWhiteSpace(SpriteOutputPath) || string.IsNullOrWhiteSpace(ThingsOutputPath))
		{
			StatusMessage = "Please select valid output paths.";
			return;
		}
		try
		{
			await _parent.CompilePairAs(SelectedArchivePair.Pair, SpriteOutputPath, ThingsOutputPath);
			ClosePanel();
		}
		catch (Exception ex)
		{
			StatusMessage = $"Compile As failed: {ex.Message}";
		}
	}
}
