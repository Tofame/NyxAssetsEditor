using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NyxAssetsEditor.Services;

namespace NyxAssetsEditor.ViewModels
{
	public record LinkedArchivePair(
		FloatingSpriteLoaderViewModel SpritePanel,
		FloatingThingsLoaderViewModel ThingsPanel);

	public partial class AssetsViewModel : ViewModelBase
	{
		private readonly SpriteRenderer _renderer = new SpriteRenderer();
		private FloatingSpriteLoaderViewModel? _lastLoadedSprPanel;
		private FloatingSpriteLoaderViewModel? _lastLoadedAssetsPanel;
		private FloatingSpriteLoaderViewModel? _lastLoadedSpritePanel;

		public ObservableCollection<PanelViewModelBase> ActivePanels { get; } = new ObservableCollection<PanelViewModelBase>();
		public ObservableCollection<PanelViewModelBase> FloatingPanels { get; } = new ObservableCollection<PanelViewModelBase>();
		public ObservableCollection<PanelViewModelBase> LeftDockedPanels { get; } = new ObservableCollection<PanelViewModelBase>();
		public ObservableCollection<PanelViewModelBase> CenterDockedPanels { get; } = new ObservableCollection<PanelViewModelBase>();
		public ObservableCollection<PanelViewModelBase> RightDockedPanels { get; } = new ObservableCollection<PanelViewModelBase>();

		public AssetsViewModel()
		{
			// Subscribe to defaults if settings change
			SettingsViewModel.DefaultPageSizeChanged += OnDefaultPageSizeChanged;

			// Restore workspace panels state
			NyxAssetsEditor.Services.PersistenceService.LoadAppState(this, _renderer);
			RefreshCompileCommands();
		}

		public Func<System.Threading.Tasks.Task>? CompileAsHandler { get; set; }

		public bool CanCompile => GetCompilePairs().Any();

		public System.Collections.Generic.IReadOnlyList<LinkedArchivePair> GetCompilePairs()
		{
			var pairs = new System.Collections.Generic.List<LinkedArchivePair>();

			foreach (var thingsPanel in ActivePanels.OfType<FloatingThingsLoaderViewModel>())
			{
				if (!thingsPanel.IsArchiveLoaded)
					continue;

				var spritePanel = ResolveSpritePanelFor(thingsPanel);
				if (spritePanel == null)
					continue;

				if (!ArchiveFormatHelper.AreCompatible(spritePanel.ArchiveFormat, thingsPanel.ArchiveFormat))
					continue;

				pairs.Add(new LinkedArchivePair(spritePanel, thingsPanel));
			}

			return pairs;
		}

		public void OnSpriteArchiveLoaded(FloatingSpriteLoaderViewModel panel)
		{
			switch (panel.ArchiveFormat)
			{
				case ArchiveFormat.Spr:
					_lastLoadedSprPanel = panel;
					break;
				case ArchiveFormat.Assets:
					_lastLoadedAssetsPanel = panel;
					break;
			}

			_lastLoadedSpritePanel = panel;

			foreach (var thingsPanel in ActivePanels.OfType<FloatingThingsLoaderViewModel>())
			{
				if (thingsPanel.ArchiveFormat != ArchiveFormat.Unknown)
					LinkThingsToSprite(thingsPanel, thingsPanel.ArchiveFormat);
				thingsPanel.RefreshPreviews();
			}

			RefreshCompileCommands();
		}

		public FloatingSpriteLoaderViewModel? ResolveSpritePanelFor(FloatingThingsLoaderViewModel thingsPanel)
		{
			var thingsFormat = thingsPanel.ArchiveFormat;
			var linked = thingsPanel.LinkedSpritePanel;

			if (linked is { IsArchiveLoaded: true }
				&& (thingsFormat == ArchiveFormat.Unknown
					|| ArchiveFormatHelper.AreCompatible(linked.ArchiveFormat, thingsFormat)))
			{
				return linked;
			}

			var candidate = thingsFormat switch
			{
				ArchiveFormat.Dat => _lastLoadedSprPanel,
				ArchiveFormat.Things => _lastLoadedAssetsPanel,
				ArchiveFormat.Unknown => _lastLoadedSpritePanel,
				_ => null
			};

			if (candidate is { IsArchiveLoaded: true }
				&& (thingsFormat == ArchiveFormat.Unknown
					|| ArchiveFormatHelper.AreCompatible(candidate.ArchiveFormat, thingsFormat)))
			{
				return candidate;
			}

			return FindLoadedSpritePanel(thingsFormat);
		}

		private FloatingSpriteLoaderViewModel? FindLoadedSpritePanel(ArchiveFormat thingsFormat)
		{
			var loaded = ActivePanels.OfType<FloatingSpriteLoaderViewModel>()
				.Where(p => p.IsArchiveLoaded)
				.ToList();

			return thingsFormat switch
			{
				ArchiveFormat.Dat => loaded.LastOrDefault(p => p.ArchiveFormat == ArchiveFormat.Spr),
				ArchiveFormat.Things => loaded.LastOrDefault(p => p.ArchiveFormat == ArchiveFormat.Assets),
				ArchiveFormat.Unknown => loaded.LastOrDefault(),
				_ => null
			};
		}

		public void LinkThingsToSprite(FloatingThingsLoaderViewModel thingsPanel, ArchiveFormat thingsFormat)
		{
			var spritePanel = thingsFormat switch
			{
				ArchiveFormat.Dat => _lastLoadedSprPanel,
				ArchiveFormat.Things => _lastLoadedAssetsPanel,
				_ => null
			};

			if (spritePanel != null
				&& spritePanel.IsArchiveLoaded
				&& ArchiveFormatHelper.AreCompatible(spritePanel.ArchiveFormat, thingsFormat))
			{
				thingsPanel.LinkedSpritePanel = spritePanel;
			}
		}

		public void RestoreThingsLink(FloatingThingsLoaderViewModel thingsPanel, string? linkedSpritePath)
		{
			if (string.IsNullOrEmpty(linkedSpritePath))
				return;

			var spritePanel = ActivePanels.OfType<FloatingSpriteLoaderViewModel>()
				.FirstOrDefault(p => p.FilePath == linkedSpritePath);

			if (spritePanel != null)
				thingsPanel.LinkedSpritePanel = spritePanel;
		}

		private void RegisterPanel(PanelViewModelBase panel)
		{
			if (panel is FloatingSpriteLoaderViewModel spritePanel)
				spritePanel.ParentViewModel = this;
		}

		private void UnregisterSpritePanel(FloatingSpriteLoaderViewModel spritePanel)
		{
			if (_lastLoadedSprPanel == spritePanel)
				_lastLoadedSprPanel = null;
			if (_lastLoadedAssetsPanel == spritePanel)
				_lastLoadedAssetsPanel = null;
			if (_lastLoadedSpritePanel == spritePanel)
				_lastLoadedSpritePanel = null;

			foreach (var thingsPanel in ActivePanels.OfType<FloatingThingsLoaderViewModel>())
			{
				if (thingsPanel.LinkedSpritePanel == spritePanel)
				{
					thingsPanel.LinkedSpritePanel = null;
					thingsPanel.RefreshPreviews();
				}
			}
		}

		private void RefreshCompileCommands()
		{
			OnPropertyChanged(nameof(CanCompile));
			CompileCommand.NotifyCanExecuteChanged();
			CompileAsCommand.NotifyCanExecuteChanged();
		}

		[RelayCommand(CanExecute = nameof(CanCompile))]
		private void Compile()
		{
			foreach (var pair in GetCompilePairs())
			{
				try
				{
					ArchiveCompileService.BackupIfExists(pair.SpritePanel.FilePath);
					ArchiveCompileService.BackupIfExists(pair.ThingsPanel.FilePath);

					ArchiveCompileService.CompilePair(
						pair.SpritePanel,
						pair.ThingsPanel,
						pair.SpritePanel.FilePath,
						pair.ThingsPanel.FilePath);

					pair.SpritePanel.LoadArchive(pair.SpritePanel.FilePath);
					pair.ThingsPanel.LoadArchive(pair.ThingsPanel.FilePath);
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"Compile failed: {ex.Message}");
				}
			}
		}

		[RelayCommand(CanExecute = nameof(CanCompile))]
		private async System.Threading.Tasks.Task CompileAs()
		{
			if (CompileAsHandler != null)
				await CompileAsHandler();
		}

		public void CompilePairAs(LinkedArchivePair pair, string spriteOutputPath, string thingsOutputPath)
		{
			ArchiveCompileService.CompilePair(pair.SpritePanel, pair.ThingsPanel, spriteOutputPath, thingsOutputPath);
			pair.SpritePanel.FilePath = spriteOutputPath;
			pair.ThingsPanel.FilePath = thingsOutputPath;
			pair.SpritePanel.LoadArchive(spriteOutputPath);
			pair.ThingsPanel.LoadArchive(thingsOutputPath);
			TriggerSaveAppState();
		}

		public void ClearAllPanels()
		{
			foreach (var panel in ActivePanels)
			{
				panel.RequestClose -= OnPanelRequestClose;
				panel.RequestDockStateChanged -= OnPanelRequestDockStateChanged;
				panel.PropertyChanged -= OnPanelPropertyChanged;
				if (panel is IDisposable disp)
				{
					disp.Dispose();
				}
			}

			ActivePanels.Clear();
			FloatingPanels.Clear();
			LeftDockedPanels.Clear();
			CenterDockedPanels.Clear();
			RightDockedPanels.Clear();
			_lastLoadedSprPanel = null;
			_lastLoadedAssetsPanel = null;
			_lastLoadedSpritePanel = null;
			UpdateColumnWidths();
			RefreshCompileCommands();
		}

		public void RestorePanel(PanelViewModelBase panel)
		{
			panel.RequestClose += OnPanelRequestClose;
			panel.RequestDockStateChanged += OnPanelRequestDockStateChanged;
			panel.PropertyChanged += OnPanelPropertyChanged;

			RegisterPanel(panel);
			ActivePanels.Add(panel);

			switch (panel.DockState)
			{
				case "Left":
					LeftDockedPanels.Add(panel);
					break;
				case "Center":
					CenterDockedPanels.Add(panel);
					break;
				case "Right":
					RightDockedPanels.Add(panel);
					break;
				default:
					FloatingPanels.Add(panel);
					break;
			}

			UpdateColumnWidths();
			RefreshCompileCommands();
		}

		private void OnDefaultPageSizeChanged(int newPageSize)
		{
			foreach (var panel in ActivePanels)
			{
				if (panel is FloatingSpriteLoaderViewModel spritePanel)
				{
					spritePanel.PageSize = newPageSize;
				}
			}
		}

		[RelayCommand]
		private void LoadAssets()
		{
			var panel = new FloatingSpriteLoaderViewModel(_renderer)
			{
				PageSize = SettingsViewModel.DefaultPageSize,
				UseTransparentPixels = SettingsViewModel.UseTransparentPixels,
				UseExtendedSpriteIds = SettingsViewModel.UseExtendedSpriteIds,
				PositionX = 100,
				PositionY = 80 + ActivePanels.Count * 25,
				IsVisible = true
			};

			AddPanel(panel);
		}

		public bool IsSpriteArchiveLoaded
		{
			get
			{
				foreach (var panel in ActivePanels)
				{
					if (panel is FloatingSpriteLoaderViewModel spritePanel && spritePanel.IsArchiveLoaded)
					{
						return true;
					}
				}
				return false;
			}
		}

		[RelayCommand]
		private void LoadThings()
		{
			if (!IsSpriteArchiveLoaded) return;

			var panel = new FloatingThingsLoaderViewModel(this)
			{
				PositionX = 100,
				PositionY = 80 + ActivePanels.Count * 25,
				IsVisible = true
			};

			AddPanel(panel);
			panel.RefreshPreviews();
		}

		private void AddPanel(PanelViewModelBase panel)
		{
			panel.RequestClose += OnPanelRequestClose;
			panel.RequestDockStateChanged += OnPanelRequestDockStateChanged;
			panel.PropertyChanged += OnPanelPropertyChanged;

			RegisterPanel(panel);
			ActivePanels.Add(panel);
			FloatingPanels.Add(panel);

			NyxAssetsEditor.Services.PersistenceService.SaveAppState(this);
			RefreshCompileCommands();
		}

		private string? _dragOverZone;

		public string? DragOverZone
		{
			get => _dragOverZone;
			set
			{
				if (SetProperty(ref _dragOverZone, value))
				{
					OnPropertyChanged(nameof(IsDragOverLeft));
					OnPropertyChanged(nameof(IsDragOverCenter));
					OnPropertyChanged(nameof(IsDragOverRight));
				}
			}
		}

		public bool IsDragOverLeft => DragOverZone == "Left";
		public bool IsDragOverCenter => DragOverZone == "Center";
		public bool IsDragOverRight => DragOverZone == "Right";

		private bool _isDraggingPanel;
		public bool IsDraggingPanel
		{
			get => _isDraggingPanel;
			set => SetProperty(ref _isDraggingPanel, value);
		}

		public bool IsLeftEmpty => LeftDockedPanels.Count == 0;
		public bool IsCenterEmpty => CenterDockedPanels.Count == 0;
		public bool IsRightEmpty => RightDockedPanels.Count == 0;

		private void UpdateColumnWidths()
		{
			OnPropertyChanged(nameof(IsLeftEmpty));
			OnPropertyChanged(nameof(IsCenterEmpty));
			OnPropertyChanged(nameof(IsRightEmpty));
		}

		public void TriggerSaveAppState()
		{
			NyxAssetsEditor.Services.PersistenceService.SaveAppState(this);
		}

		private void OnPanelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(FloatingSpriteLoaderViewModel.IsArchiveLoaded))
			{
				OnPropertyChanged(nameof(IsSpriteArchiveLoaded));
				foreach (var panel in ActivePanels)
				{
					if (panel is FloatingThingsLoaderViewModel thingsPanel)
					{
						thingsPanel.RefreshPreviews();
					}
				}
				RefreshCompileCommands();
			}

			if (e.PropertyName == nameof(FloatingThingsLoaderViewModel.IsArchiveLoaded))
			{
				RefreshCompileCommands();
			}

			if (e.PropertyName == nameof(PanelViewModelBase.IsMinimized) ||
				e.PropertyName == "FilePath" ||
				e.PropertyName == "IsGridView" ||
				e.PropertyName == "PageSize" ||
				e.PropertyName == "CurrentPage")
			{
				NyxAssetsEditor.Services.PersistenceService.SaveAppState(this);
			}
		}

		private void OnPanelRequestClose(PanelViewModelBase panel)
		{
			panel.RequestClose -= OnPanelRequestClose;
			panel.RequestDockStateChanged -= OnPanelRequestDockStateChanged;
			panel.PropertyChanged -= OnPanelPropertyChanged;

			if (panel is IDisposable disp)
			{
				disp.Dispose();
			}

			if (panel is FloatingSpriteLoaderViewModel spritePanel)
				UnregisterSpritePanel(spritePanel);

			ActivePanels.Remove(panel);
			RemoveFromDockCollections(panel);
			UpdateColumnWidths();
			OnPropertyChanged(nameof(IsSpriteArchiveLoaded));
			RefreshCompileCommands();

			NyxAssetsEditor.Services.PersistenceService.SaveAppState(this);
		}

		private void OnPanelRequestDockStateChanged(PanelViewModelBase panel)
		{
			RemoveFromDockCollections(panel);

			switch (panel.DockState)
			{
				case "Left":
					LeftDockedPanels.Add(panel);
					break;
				case "Center":
					CenterDockedPanels.Add(panel);
					break;
				case "Right":
					RightDockedPanels.Add(panel);
					break;
				default:
					FloatingPanels.Add(panel);
					break;
			}
			UpdateColumnWidths();

			NyxAssetsEditor.Services.PersistenceService.SaveAppState(this);
		}

		private void RemoveFromDockCollections(PanelViewModelBase panel)
		{
			FloatingPanels.Remove(panel);
			LeftDockedPanels.Remove(panel);
			CenterDockedPanels.Remove(panel);
			RightDockedPanels.Remove(panel);
		}
	}
}
