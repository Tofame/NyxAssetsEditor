using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NyxAssetsEditor.Services.Archive;
using NyxAssetsEditor.Services.Persistence;
using NyxAssetsEditor.Services.Rendering;
using NyxAssetsEditor.Services.Things;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;
using NyxAssetsEditor.ViewModels.Common;
using NyxAssetsEditor.ViewModels.Core;

namespace NyxAssetsEditor.ViewModels.Pages
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
		private FloatingSpriteLoaderViewModel? _pendingSprForNextDat;
		private FloatingSpriteLoaderViewModel? _pendingAssetsForNextThings;

		public ObservableCollection<PanelViewModelBase> ActivePanels { get; } = new ObservableCollection<PanelViewModelBase>();
		public ObservableCollection<PanelViewModelBase> FloatingPanels { get; } = new ObservableCollection<PanelViewModelBase>();
		public ObservableCollection<PanelViewModelBase> LeftDockedPanels { get; } = new ObservableCollection<PanelViewModelBase>();
		public ObservableCollection<PanelViewModelBase> CenterDockedPanels { get; } = new ObservableCollection<PanelViewModelBase>();
		public ObservableCollection<PanelViewModelBase> RightDockedPanels { get; } = new ObservableCollection<PanelViewModelBase>();

		public AssetsViewModel()
		{
			// Subscribe to defaults if settings change
			SettingsViewModel.DefaultPageSizeChanged += OnDefaultPageSizeChanged;

			_ = PersistenceService.LoadAppStateAsync(this, _renderer);
			RefreshCompileCommands();
		}

		public Func<System.Threading.Tasks.Task>? CompileAsHandler { get; set; }
		public bool CanCompile => GetCompilePairs().Any() && GetCompilePairs().Any(p => p.ThingsPanel.HasSavedChanges || p.SpritePanel.HasSavedChanges);

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
					_pendingSprForNextDat = panel;
					break;
				case ArchiveFormat.Assets:
					_lastLoadedAssetsPanel = panel;
					_pendingAssetsForNextThings = panel;
					break;
			}

			_lastLoadedSpritePanel = panel;

			foreach (var thingsPanel in ActivePanels.OfType<FloatingThingsLoaderViewModel>())
			{
				if (thingsPanel.LinkedSpritePanel == panel)
					thingsPanel.NotifySpriteLinkChanged();
			}

			RefreshCompileCommands();
		}

		public bool HasAnyPendingSpriteForThings() =>
			_pendingSprForNextDat is { IsArchiveLoaded: true }
			|| _pendingAssetsForNextThings is { IsArchiveLoaded: true };

		public bool HasPendingSpriteFor(ArchiveFormat thingsFormat) =>
			thingsFormat switch
			{
				ArchiveFormat.Dat => _pendingSprForNextDat is { IsArchiveLoaded: true },
				ArchiveFormat.Things => _pendingAssetsForNextThings is { IsArchiveLoaded: true },
				_ => false,
			};

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

			return null;
		}

		/// <summary>
		/// Pairs a things panel with the most recently loaded, unconsumed sprite archive of the matching format.
		/// Each sprite load is consumed by at most one new dat/things load.
		/// </summary>
		public bool TryAssignPendingSpriteLink(FloatingThingsLoaderViewModel thingsPanel, ArchiveFormat thingsFormat)
		{
			var pending = thingsFormat switch
			{
				ArchiveFormat.Dat => _pendingSprForNextDat,
				ArchiveFormat.Things => _pendingAssetsForNextThings,
				_ => null,
			};

			if (pending == null
				|| !pending.IsArchiveLoaded
				|| !ArchiveFormatHelper.AreCompatible(pending.ArchiveFormat, thingsFormat))
			{
				return false;
			}

			thingsPanel.LinkedSpritePanel = pending;

			switch (thingsFormat)
			{
				case ArchiveFormat.Dat:
					if (_pendingSprForNextDat == pending)
						_pendingSprForNextDat = null;
					break;
				case ArchiveFormat.Things:
					if (_pendingAssetsForNextThings == pending)
						_pendingAssetsForNextThings = null;
					break;
			}

			NotifyThingsPanelsSpriteAvailabilityChanged();
			return true;
		}

		private void NotifyThingsPanelsSpriteAvailabilityChanged()
		{
			foreach (var thingsPanel in ActivePanels.OfType<FloatingThingsLoaderViewModel>())
				thingsPanel.NotifySpriteLinkChanged();
			RefreshLooktypeGenerators();
		}

		public void RefreshLooktypeGenerators()
		{
			foreach (var generator in ActivePanels.OfType<FloatingLooktypeGeneratorViewModel>())
				generator.RefreshArchivePairs();
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
			if (_pendingSprForNextDat == spritePanel)
				_pendingSprForNextDat = null;
			if (_pendingAssetsForNextThings == spritePanel)
				_pendingAssetsForNextThings = null;

			foreach (var thingsPanel in ActivePanels.OfType<FloatingThingsLoaderViewModel>())
			{
				if (thingsPanel.LinkedSpritePanel == spritePanel)
				{
					thingsPanel.LinkedSpritePanel = null;
					thingsPanel.NotifySpriteLinkChanged();
				}
			}
		}
		public void RefreshCompileCommands()
		{
			OnPropertyChanged(nameof(CanCompile));
			CompileCommand.NotifyCanExecuteChanged();
			CompileAsCommand.NotifyCanExecuteChanged();
		}

		/// <summary>
		/// Returns a human-readable summary of pending changes across all dirty pairs.
		/// </summary>
		public string GetPendingChangesSummary()
		{
			var lines = new System.Collections.Generic.List<string>();

			foreach (var pair in GetCompilePairs())
			{
				if (!pair.ThingsPanel.HasSavedChanges && !pair.SpritePanel.HasSavedChanges)
					continue;

				var fileName = pair.ThingsPanel.FileName;
				if (!string.IsNullOrEmpty(fileName))
					lines.Add($"Archive: {fileName}");

				if (pair.ThingsPanel.HasSavedChanges)
				{
					lines.Add($"  Things changes:");
					if (pair.ThingsPanel.AddedThingIds.Count > 0)
						lines.Add($"    Added ({pair.ThingsPanel.AddedThingIds.Count}): {string.Join(", ", pair.ThingsPanel.AddedThingIds)}");
					if (pair.ThingsPanel.ModifiedThingIds.Count > 0)
						lines.Add($"    Modified ({pair.ThingsPanel.ModifiedThingIds.Count}): {string.Join(", ", pair.ThingsPanel.ModifiedThingIds)}");
					if (pair.ThingsPanel.RemovedThingIds.Count > 0)
						lines.Add($"    Removed ({pair.ThingsPanel.RemovedThingIds.Count}): {string.Join(", ", pair.ThingsPanel.RemovedThingIds)}");
				}

				if (pair.SpritePanel.HasSavedChanges)
				{
					var sprFile = pair.SpritePanel.FileName;
					lines.Add($"  Sprites changes" + (string.IsNullOrEmpty(sprFile) ? ":" : $" ({sprFile}):"));
					if (pair.SpritePanel.AddedSpriteIds.Count > 0)
						lines.Add($"    Added ({pair.SpritePanel.AddedSpriteIds.Count}): {string.Join(", ", pair.SpritePanel.AddedSpriteIds)}");
					if (pair.SpritePanel.ModifiedSpriteIds.Count > 0)
						lines.Add($"    Modified ({pair.SpritePanel.ModifiedSpriteIds.Count}): {string.Join(", ", pair.SpritePanel.ModifiedSpriteIds)}");
					if (pair.SpritePanel.RemovedSpriteIds.Count > 0)
						lines.Add($"    Removed ({pair.SpritePanel.RemovedSpriteIds.Count}): {string.Join(", ", pair.SpritePanel.RemovedSpriteIds)}");
				}
			}

			return lines.Count > 0
				? string.Join("\n", lines)
				: "Changes pending (details unavailable).";
		}

		[RelayCommand(CanExecute = nameof(CanCompile))]
		private async System.Threading.Tasks.Task Compile()
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

					// Await sprite reload first so the link is available for things reload
					await pair.SpritePanel.LoadArchiveAsync(pair.SpritePanel.FilePath);
					await pair.ThingsPanel.LoadArchiveAsync(pair.ThingsPanel.FilePath, useLastLoadedSprite: false);

					pair.SpritePanel.HasSavedChanges = false;
					pair.ThingsPanel.HasSavedChanges = false;
					RefreshCompileCommands();
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"Compile failed: {ex}");
				}
			}
		}

		[RelayCommand(CanExecute = nameof(CanCompile))]
		private async System.Threading.Tasks.Task CompileAs()
		{
			if (CompileAsHandler != null)
				await CompileAsHandler();
		}

		public async System.Threading.Tasks.Task CompilePairAs(LinkedArchivePair pair, string spriteOutputPath, string thingsOutputPath)
		{
			ArchiveCompileService.CompilePair(pair.SpritePanel, pair.ThingsPanel, spriteOutputPath, thingsOutputPath);
			pair.SpritePanel.FilePath = spriteOutputPath;
			pair.ThingsPanel.FilePath = thingsOutputPath;
			await pair.SpritePanel.LoadArchiveAsync(spriteOutputPath);
			await pair.ThingsPanel.LoadArchiveAsync(thingsOutputPath, useLastLoadedSprite: false);
			pair.SpritePanel.HasSavedChanges = false;
			pair.ThingsPanel.HasSavedChanges = false;
			RefreshCompileCommands();
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
			_pendingSprForNextDat = null;
			_pendingAssetsForNextThings = null;
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
		private async System.Threading.Tasks.Task NewArchive()
		{
			var desktop = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
			if (desktop?.MainWindow is not Avalonia.Controls.Window mainWindow) return;

			var dialog = new NyxAssetsEditor.Views.Shell.NewArchiveDialog();
			await dialog.ShowDialog(mainWindow);

			if (dialog.Result.IsConfirmed)
			{
				var spritePanel = new FloatingSpriteLoaderViewModel(_renderer)
				{
					PageSize = SettingsViewModel.DefaultPageSize,
					UseTransparentPixels = dialog.Result.UseTransparentPixels,
					UseExtendedSpriteIds = dialog.Result.UseExtendedSpriteIds,
					PositionX = 100,
					PositionY = 80 + ActivePanels.Count * 25,
					IsVisible = true
				};

				AddPanel(spritePanel);

				string sprFormat = dialog.Result.Format == "dat" ? "spr" : "assets";
				await spritePanel.CreateNewArchiveAsync(sprFormat, dialog.Result.ClientVersion, dialog.Result.UseExtendedSpriteIds, dialog.Result.UseTransparentPixels);

				var thingsPanel = new FloatingThingsLoaderViewModel(this)
				{
					PositionX = 100,
					PositionY = 80 + ActivePanels.Count * 25,
					IsVisible = true
				};

				AddPanel(thingsPanel);

				string thingsFormat = dialog.Result.Format == "dat" ? "dat" : "things";
				await thingsPanel.CreateNewArchiveAsync(thingsFormat, dialog.Result.ClientVersion, dialog.Result.UseExtendedSpriteIds, dialog.Result.UseFrameAnimations, dialog.Result.UseFrameGroups);

				thingsPanel.LinkedSpritePanel = spritePanel;
				thingsPanel.NotifySpriteLinkChanged();

				RefreshCompileCommands();
				PersistenceService.SaveAppState(this);
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
			var panel = new FloatingThingsLoaderViewModel(this)
			{
				PositionX = 100,
				PositionY = 80 + ActivePanels.Count * 25,
				IsVisible = true
			};

			AddPanel(panel);
			panel.NotifySpriteLinkChanged();
		}

		[RelayCommand]
		private void OpenLooktypeGenerator()
		{
			var existing = ActivePanels.OfType<FloatingLooktypeGeneratorViewModel>().FirstOrDefault();
			if (existing != null)
			{
				existing.IsVisible = true;
				existing.IsMinimized = false;
				existing.RefreshArchivePairs();
				return;
			}

			AddPanel(new FloatingLooktypeGeneratorViewModel(this)
			{
				PositionX = 60,
				PositionY = 60,
				IsVisible = true,
			});
		}

		private void AddPanel(PanelViewModelBase panel)
		{
			panel.RequestClose += OnPanelRequestClose;
			panel.RequestDockStateChanged += OnPanelRequestDockStateChanged;
			panel.PropertyChanged += OnPanelPropertyChanged;

			RegisterPanel(panel);
			ActivePanels.Add(panel);
			FloatingPanels.Add(panel);

			PersistenceService.SaveAppState(this);
			RefreshCompileCommands();
			RefreshLooktypeGenerators();
		}

		public void OpenThingFinder(FloatingThingsLoaderViewModel source)
		{
			if (!source.IsArchiveLoaded) return;
			var existing = ActivePanels.OfType<FloatingThingFinderViewModel>()
				.FirstOrDefault(panel => ReferenceEquals(panel.SourcePanel, source));
			if (existing != null)
			{
				existing.IsVisible = true;
				existing.IsMinimized = false;
				if (existing.IsFloating)
				{
					FloatingPanels.Remove(existing);
					FloatingPanels.Add(existing);
				}
				return;
			}

			AddPanel(new FloatingThingFinderViewModel(this, source)
			{
				IsVisible = true,
			});
		}

		public IReadOnlyList<ThingFinderContextAction> GetThingFinderContextActions(
			FloatingThingsLoaderViewModel source,
			NyxAssets.Things.ThingType thing) => ActivePanels
			.Where(panel => panel.IsVisible)
			.OfType<IThingFinderContextActionProvider>()
			.SelectMany(provider => provider.GetThingFinderContextActions(source, thing))
			.ToList();

		public async System.Threading.Tasks.Task OpenThingEditor(FloatingThingsLoaderViewModel source, uint thingId, bool newWindow = false)
		{
			var thing = source.GetThingType(thingId);
			if (thing == null)
				return;

			if (!newWindow)
			{
				var existing = ActivePanels.OfType<FloatingThingEditorViewModel>()
					.FirstOrDefault(p => ReferenceEquals(p.SourcePanel, source));
				if (existing != null)
				{
					if (existing.IsDirty && existing.ThingId != thingId)
					{
						var tcs = new System.Threading.Tasks.TaskCompletionSource<FloatingThingEditorViewModel.PromptResult>();
						existing.ShowPrompt(
							"Save Changes?",
							$"Save changes done to thing {existing.ThingId}?",
							tcs);
						var result = await tcs.Task;
						if (result == FloatingThingEditorViewModel.PromptResult.Save)
						{
							existing.Save();
						}
						else if (result == FloatingThingEditorViewModel.PromptResult.Cancel)
						{
							var previousItem = source.PagedThings.FirstOrDefault(t => t.Id == existing.ThingId);
							if (previousItem != null)
							{
								source.SelectThing(previousItem);
							}
							return;
						}
					}
					existing.LoadThing(thing);
					existing.IsVisible = true;
					existing.IsMinimized = false;
					return;
				}
			}

			var panel = new FloatingThingEditorViewModel(source, thing)
			{
				PositionX = source.PositionX + 40,
				PositionY = source.PositionY + 40,
				IsVisible = true,
			};
			AddPanel(panel);
		}

		public void OpenMultiThingEditor(FloatingThingsLoaderViewModel source, IEnumerable<uint> thingIds)
		{
			var things = thingIds.Distinct().Select(source.GetThingType).Where(t => t != null).Cast<NyxAssets.Things.ThingType>().ToList();
			if (things.Count < 2) return;

			var existing = ActivePanels.OfType<FloatingMultiThingEditorViewModel>()
				.FirstOrDefault(p => ReferenceEquals(p.SourcePanel, source));
			if (existing != null)
			{
				existing.IsVisible = true;
				existing.IsMinimized = false;
				return;
			}

			AddPanel(new FloatingMultiThingEditorViewModel(source, things));
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
			PersistenceService.SaveAppState(this);
		}

		private void OnPanelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(FloatingSpriteLoaderViewModel.IsArchiveLoaded)
				&& sender is FloatingSpriteLoaderViewModel spritePanel)
			{
				OnPropertyChanged(nameof(IsSpriteArchiveLoaded));
				foreach (var thingsPanel in ActivePanels.OfType<FloatingThingsLoaderViewModel>())
				{
					if (thingsPanel.LinkedSpritePanel == spritePanel)
						thingsPanel.NotifySpriteLinkChanged();
				}
				RefreshCompileCommands();
				RefreshLooktypeGenerators();
			}

			if (e.PropertyName == nameof(FloatingThingsLoaderViewModel.IsArchiveLoaded))
			{
				RefreshCompileCommands();
				RefreshLooktypeGenerators();
			}

			if (e.PropertyName == "HasSavedChanges"
				&& sender is FloatingSpriteLoaderViewModel or FloatingThingsLoaderViewModel)
			{
				RefreshLooktypeGenerators();
			}

			if (e.PropertyName == nameof(PanelViewModelBase.IsMinimized) ||
				e.PropertyName == "FilePath" ||
				e.PropertyName == "IsGridView" ||
				e.PropertyName == "PageSize" ||
				e.PropertyName == "CurrentPage")
			{
				PersistenceService.SaveAppState(this);
			}
		}

		private async void OnPanelRequestClose(PanelViewModelBase panel)
		{
			bool hasChanges = false;
			string summaryText = string.Empty;

			if (panel is FloatingThingsLoaderViewModel thPanelUnsaved && thPanelUnsaved.HasSavedChanges)
			{
				hasChanges = true;
				var sb = new System.Text.StringBuilder();
				sb.AppendLine($"Things Panel: {System.IO.Path.GetFileName(thPanelUnsaved.FilePath)}");
				if (thPanelUnsaved.AddedThingIds.Count > 0)
					sb.AppendLine($"  Added ({thPanelUnsaved.AddedThingIds.Count}): {string.Join(", ", thPanelUnsaved.AddedThingIds)}");
				if (thPanelUnsaved.ModifiedThingIds.Count > 0)
					sb.AppendLine($"  Modified ({thPanelUnsaved.ModifiedThingIds.Count}): {string.Join(", ", thPanelUnsaved.ModifiedThingIds)}");
				if (thPanelUnsaved.RemovedThingIds.Count > 0)
					sb.AppendLine($"  Removed ({thPanelUnsaved.RemovedThingIds.Count}): {string.Join(", ", thPanelUnsaved.RemovedThingIds)}");
				summaryText = sb.ToString();
			}
			else if (panel is FloatingSpriteLoaderViewModel sprPanelUnsaved && sprPanelUnsaved.HasSavedChanges)
			{
				hasChanges = true;
				var sb = new System.Text.StringBuilder();
				sb.AppendLine($"Sprites Panel: {System.IO.Path.GetFileName(sprPanelUnsaved.FilePath)}");
				if (sprPanelUnsaved.AddedSpriteIds.Count > 0)
					sb.AppendLine($"  Added ({sprPanelUnsaved.AddedSpriteIds.Count}): {string.Join(", ", sprPanelUnsaved.AddedSpriteIds)}");
				if (sprPanelUnsaved.ModifiedSpriteIds.Count > 0)
					sb.AppendLine($"  Modified ({sprPanelUnsaved.ModifiedSpriteIds.Count}): {string.Join(", ", sprPanelUnsaved.ModifiedSpriteIds)}");
				if (sprPanelUnsaved.RemovedSpriteIds.Count > 0)
					sb.AppendLine($"  Removed ({sprPanelUnsaved.RemovedSpriteIds.Count}): {string.Join(", ", sprPanelUnsaved.RemovedSpriteIds)}");
				summaryText = sb.ToString();
			}

			if (hasChanges)
			{
				panel.IsVisible = true;
				var desktop = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
				if (desktop?.MainWindow is Avalonia.Controls.Window mainWindow)
				{
					var dialog = new NyxAssetsEditor.Views.Shell.PanelUnsavedChangesDialog("Unsaved Changes", summaryText);
					await dialog.ShowDialog(mainWindow);

					if (dialog.Result == NyxAssetsEditor.Views.Shell.PanelUnsavedChangesResult.Cancel)
					{
						return;
					}
					else if (dialog.Result == NyxAssetsEditor.Views.Shell.PanelUnsavedChangesResult.Discard)
					{
						if (panel is FloatingThingsLoaderViewModel thDiscard)
							thDiscard.DiscardChanges();
						else if (panel is FloatingSpriteLoaderViewModel sprDiscard)
							sprDiscard.DiscardChanges();

						panel.IsVisible = false;
					}
				}
				else
				{
					return;
				}
			}

			if (panel is FloatingSpriteLoaderViewModel sprPanelClose)
			{
				var linkedThingsPanels = ActivePanels
					.OfType<FloatingThingsLoaderViewModel>()
					.Where(tp => tp.LinkedSpritePanel == sprPanelClose)
					.ToList();

				if (linkedThingsPanels.Count > 0)
				{
					var desktop = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
					if (desktop?.MainWindow is Avalonia.Controls.Window mainWindow)
					{
						bool hasUnsaved = linkedThingsPanels.Any(tp => tp.HasSavedChanges);
						var summary = string.Join("\n", linkedThingsPanels.Select(p => $"• {System.IO.Path.GetFileName(p.FilePath)} (ID: {p.GetHashCode()}){(p.HasSavedChanges ? " *UNSAVED CHANGES*" : "")}"));
						var dialog = new NyxAssetsEditor.Views.Shell.SpritesCloseDialog(summary, hasUnsaved);
						await dialog.ShowDialog(mainWindow);

						if (dialog.Result == NyxAssetsEditor.Views.Shell.SpritesCloseResult.Cancel)
						{
							return;
						}
						else if (dialog.Result == NyxAssetsEditor.Views.Shell.SpritesCloseResult.CloseBoth)
						{
							// Close all linked things panels first
							foreach (var tp in linkedThingsPanels)
							{
								OnPanelRequestClose(tp);
							}
						}
					}
					else
					{
						foreach (var tp in linkedThingsPanels)
						{
							OnPanelRequestClose(tp);
						}
					}
				}
			}

			if (panel is FloatingThingsLoaderViewModel sourcePanel)
			{
				foreach (var finder in ActivePanels.OfType<FloatingThingFinderViewModel>()
					.Where(candidate => ReferenceEquals(candidate.SourcePanel, sourcePanel)).ToList())
				{
					finder.ClosePanel();
				}
			}

			panel.RequestClose -= OnPanelRequestClose;
			panel.RequestDockStateChanged -= OnPanelRequestDockStateChanged;
			panel.PropertyChanged -= OnPanelPropertyChanged;

			if (panel is IDisposable disp)
			{
				disp.Dispose();
			}

			if (panel is FloatingSpriteLoaderViewModel sprPanelUnregister)
				UnregisterSpritePanel(sprPanelUnregister);

			ActivePanels.Remove(panel);
			RemoveFromDockCollections(panel);
			UpdateColumnWidths();
			OnPropertyChanged(nameof(IsSpriteArchiveLoaded));
			RefreshCompileCommands();
			RefreshLooktypeGenerators();

			PersistenceService.SaveAppState(this);
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

			PersistenceService.SaveAppState(this);
		}

		private void RemoveFromDockCollections(PanelViewModelBase panel)
		{
			FloatingPanels.Remove(panel);
			LeftDockedPanels.Remove(panel);
			CenterDockedPanels.Remove(panel);
			RightDockedPanels.Remove(panel);
		}

		public async void LoadCombination(
			string spritePath,
			string thingsPath,
			bool spriteGuess = true,
			bool spritePreferOtfi = false,
			bool spriteTransparent = true,
			bool spriteExtended = true,
			bool thingsGuess = true,
			bool thingsPreferOtfi = false,
			bool thingsExtended = true,
			bool thingsAnimations = true,
			bool thingsGroups = true)
		{
			FloatingSpriteLoaderViewModel? spritePanel = null;
			FloatingThingsLoaderViewModel? thingsPanel = null;

			if (!string.IsNullOrEmpty(spritePath))
			{
				spritePanel = new FloatingSpriteLoaderViewModel(_renderer)
				{
					PageSize = SettingsViewModel.DefaultPageSize,
					GuessSettingsFromSignature = spriteGuess,
					PreferOtfiSettings = spritePreferOtfi,
					UseTransparentPixels = spriteTransparent,
					UseExtendedSpriteIds = spriteExtended,
					PositionX = 100,
					PositionY = 100,
					IsVisible = true
				};
				AddPanel(spritePanel);
			}

			if (!string.IsNullOrEmpty(thingsPath))
			{
				thingsPanel = new FloatingThingsLoaderViewModel(this)
				{
					GuessSettingsFromSignature = thingsGuess,
					PreferOtfiSettings = thingsPreferOtfi,
					UseExtendedThingIds = thingsExtended,
					UseFrameAnimations = thingsAnimations,
					UseFrameGroups = thingsGroups,
					PositionX = 100,
					PositionY = 100,
					IsVisible = true
				};
				AddPanel(thingsPanel);
			}

			if (spritePanel != null)
			{
				await spritePanel.LoadArchiveAsync(spritePath);
			}

			if (thingsPanel != null)
			{
				if (spritePanel != null)
				{
					var thingsFormat = ArchiveFormatHelper.FromPath(thingsPath);
					if (ArchiveFormatHelper.AreCompatible(spritePanel.ArchiveFormat, thingsFormat))
					{
						thingsPanel.LinkedSpritePanel = spritePanel;
						thingsPanel.NotifySpriteLinkChanged();
					}
				}

				await thingsPanel.LoadArchiveAsync(thingsPath, useLastLoadedSprite: spritePanel == null);
			}
		}
	}
}
