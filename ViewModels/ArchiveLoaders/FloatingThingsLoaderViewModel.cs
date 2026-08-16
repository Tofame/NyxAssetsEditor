using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using NyxAssets.Things;
using NyxAssets.Things.Exchange;
using NyxAssetsEditor.Services.Archive;
using NyxAssetsEditor.Services.Exchange;
using NyxAssetsEditor.Services.Rendering;
using NyxAssetsEditor.ViewModels.Common;
using NyxAssetsEditor.ViewModels.Core;
using NyxAssetsEditor.ViewModels.Pages;
using NyxAssetsEditor.ViewModels.Things;

namespace NyxAssetsEditor.ViewModels.ArchiveLoaders
{
	public partial class ThingItemViewModel : ViewModelBase
	{
		private readonly FloatingThingsLoaderViewModel _panel;
		private Avalonia.Media.Imaging.WriteableBitmap? _previewImage;
		private bool _isSelected;
		private bool _previewRequested;

		public uint Id { get; }

		public uint DisplayedId => _panel.GetDisplayedId(Id);

		public bool IsSelected
		{
			get => _isSelected;
			set => SetProperty(ref _isSelected, value);
		}
		public bool CanBatchEdit => IsSelected && _panel.GetSelectedThings().Count > 1;
		public void NotifySelectionContextChanged() => OnPropertyChanged(nameof(CanBatchEdit));

		public Avalonia.Media.Imaging.WriteableBitmap? PreviewImage
		{
			get
			{
				if (_previewImage == null && !_previewRequested)
					LoadPreview();
				return _previewImage;
			}
		}

		public ThingItemViewModel(uint id, FloatingThingsLoaderViewModel panel)
		{
			Id = id;
			_panel = panel;
		}

		private int _currentFrame = 0;

		public void StepAnimation()
		{
			var thing = _panel.GetThingType(Id);
			if (thing == null || thing.FrameGroups.Count == 0)
				return;

			var maxFrames = thing.Kind == ThingKind.Outfit && thing.FrameGroups.Count > 1
				? (int)(thing.FrameGroups[0].Frames + thing.FrameGroups[1].Frames)
				: (int)thing.FrameGroups[0].Frames;
			if (maxFrames <= 1)
				return;

			_currentFrame = (_currentFrame + 1) % maxFrames;
			var loader = _panel.GetActiveSpriteLoader();
			if (loader != null)
			{
				_previewImage = _panel.GetPreviewForThing(thing, _currentFrame);
				OnPropertyChanged(nameof(PreviewImage));
			}
		}

		public void ResetAnimation()
		{
			if (_currentFrame != 0)
			{
				_currentFrame = 0;
				InvalidatePreview();
			}
		}

		public void InvalidatePreview()
		{
			_previewImage = null;
			_previewRequested = false;
			OnPropertyChanged(nameof(PreviewImage));
		}

		private void LoadPreview()
		{
			_previewRequested = true;
			var thing = _panel.GetThingType(Id);
			if (thing == null)
				return;

			_previewImage = _panel.GetPreviewForThing(thing, _panel.AnimateAll ? _currentFrame : 0);
			OnPropertyChanged(nameof(PreviewImage));
		}

		public void NotifyDisplayedIdChanged() => OnPropertyChanged(nameof(DisplayedId));

		[RelayCommand]
		private void Replace() => WithSelection(_panel.RequestReplaceThings, _panel.RequestReplaceThing);

		[RelayCommand]
		private void Edit()
		{
			var selected = _panel.GetSelectedThings();
			if (selected.Count > 1 && selected.Any(t => t.Id == Id))
				_panel.OpenMultiThingEditor(selected);
			else
				_ = _panel.OpenThingEditor(this);
		}

		[RelayCommand]
		private void OpenInNewWindow() => WithSelection(
			things =>
			{
				foreach (var item in things)
					_panel.OpenThingEditor(item, newWindow: true);
			},
			item => _panel.OpenThingEditor(item, newWindow: true));

		[RelayCommand]
		private void ExportPng() => ExportWithSelection("png");

		[RelayCommand]
		private void ExportJpeg() => ExportWithSelection("jpg");

		[RelayCommand]
		private void ExportBmp() => ExportWithSelection("bmp");

		[RelayCommand]
		private void ExportNyxThing() => ExportWithSelection("nyx-thing");

		[RelayCommand]
		private void ExportObd() => ExportWithSelection("obd");

		[RelayCommand]
		private void Export()
		{
			var selected = _panel.GetSelectedThings();
			if (selected.Count > 1 && selected.Any(t => t.Id == Id))
				_panel.RequestExportThings(selected);
			else
				_panel.RequestExportThing(this);
		}

		[RelayCommand]
		private void Duplicate() => WithSelection(_panel.DuplicateThings, _panel.DuplicateThing);

		[RelayCommand]
		private void Remove() => WithSelection(_panel.RemoveThings, _panel.RemoveThing);

		private void ExportWithSelection(string format)
		{
			var selected = _panel.GetSelectedThings();
			if (selected.Count > 1 && selected.Any(t => t.Id == Id))
				_panel.RequestExportThings(selected, format);
			else
				_panel.RequestExportThing(this, format);
		}

		private void WithSelection(Action<IEnumerable<ThingItemViewModel>> batch, Action<ThingItemViewModel> single)
		{
			var selected = _panel.GetSelectedThings();
			if (selected.Count > 1 && selected.Any(t => t.Id == Id))
				batch(selected);
			else
				single(this);
		}
	}

	public partial class FloatingThingsLoaderViewModel : PanelViewModelBase, IDisposable
	{


		private readonly SpriteRenderer _renderer = new SpriteRenderer();
		private readonly AssetsViewModel? _parentViewModel;
		private ThingCatalog? _catalog;
		private readonly List<ThingType> _allThings = new List<ThingType>();

		private string _filePath = "No things loaded";
		private uint _totalThings;
		private int _currentPage = 1;
		private int _pageSize = 100;
		private bool _useExtendedThingIds = true;
		private bool _useFrameAnimations = true;
		private bool _useFrameGroups = true;
		private uint _clientVersion = SettingsViewModel.ClientVersion;
		private bool _guessSettingsFromSignature = false;
		private bool _preferOtfiSettings = true;
		private string _jumpToIdText = string.Empty;
		private Services.Archive.UndoRedoStack<Services.Archive.ThingUndoAction>? _undoRedoStack;
		private Services.Archive.ThingUndoAction? _currentAction;
		private bool _hideEmpty;
		private bool _animateAll;

		public bool HideEmpty
		{
			get => _hideEmpty;
			set
			{
				if (SetProperty(ref _hideEmpty, value))
				{
					ReloadThingsForSection();
				}
			}
		}

		public bool AnimateAll
		{
			get => _animateAll;
			set
			{
				if (SetProperty(ref _animateAll, value))
				{
					if (value)
					{
						StartAnimateAllTimer();
					}
					else
					{
						StopAnimateAllTimer();
						foreach (var item in PagedThings)
						{
							item.ResetAnimation();
						}
					}
				}
			}
		}

		private ThingKind _selectedSection = ThingKind.Item;

		private ThingItemViewModel? _selectionAnchor;

		public ThingKind SelectedSection
		{
			get => _selectedSection;
			set
			{
				if (SetProperty(ref _selectedSection, value))
				{
					NotifySectionProperties();
					ReloadThingsForSection();
					GoToIdCommand.NotifyCanExecuteChanged();
				}
			}
		}

		public bool IsItemsSection => SelectedSection == ThingKind.Item;
		public bool IsOutfitsSection => SelectedSection == ThingKind.Outfit;
		public bool IsEffectsSection => SelectedSection == ThingKind.Effect;
		public bool IsMissilesSection => SelectedSection == ThingKind.Missile;

		public bool IsSectionEmpty => IsArchiveLoaded && TotalThings == 0;

		public bool ShowThingList => IsArchiveLoaded && TotalThings > 0;

		public bool ShowListViewContent => ShowThingList && IsListView;

		public bool ShowGridViewContent => ShowThingList && IsGridView;

		public string SectionLabel => SelectedSection switch
		{
			ThingKind.Item => "item",
			ThingKind.Outfit => "outfit",
			ThingKind.Effect => "effect",
			ThingKind.Missile => "missile",
			_ => "thing",
		};

		public string SectionLabelPlural => SelectedSection switch
		{
			ThingKind.Item => "items",
			ThingKind.Outfit => "outfits",
			ThingKind.Effect => "effects",
			ThingKind.Missile => "missiles",
			_ => "things",
		};

		public uint GetDisplayedId(uint id) => GetDisplayedId(SelectedSection, id);

		public uint GetDisplayedId(ThingKind kind, uint id) =>
			kind == ThingKind.Item ? id + SettingsViewModel.ThingIdOffset : id;

		[RelayCommand]
		private void SelectItemsSection() => SelectedSection = ThingKind.Item;

		[RelayCommand]
		private void SelectOutfitsSection() => SelectedSection = ThingKind.Outfit;

		[RelayCommand]
		private void SelectEffectsSection() => SelectedSection = ThingKind.Effect;

		[RelayCommand]
		private void SelectMissilesSection() => SelectedSection = ThingKind.Missile;

		[RelayCommand]
		private void FindThing()
		{
			if (IsArchiveLoaded)
				_parentViewModel?.OpenThingFinder(this);
		}

		private void NotifySectionProperties()
		{
			OnPropertyChanged(nameof(IsItemsSection));
			OnPropertyChanged(nameof(IsOutfitsSection));
			OnPropertyChanged(nameof(IsEffectsSection));
			OnPropertyChanged(nameof(IsMissilesSection));
			OnPropertyChanged(nameof(SectionLabel));
			OnPropertyChanged(nameof(SectionLabelPlural));
			OnPropertyChanged(nameof(IsSectionEmpty));
		}

		public event EventHandler<ThingFileRequestEventArgs>? RequestThingFileDialog;
		public event Action? CatalogChanged;

		public ThingItemViewModel? SelectedThing { get; private set; }

		public ObservableCollection<ThingItemViewModel> PagedThings { get; } = new ObservableCollection<ThingItemViewModel>();

		public string FilePath
		{
			get => _filePath;
			set
			{
				if (SetProperty(ref _filePath, value))
				{
					OnPropertyChanged(nameof(FileName));
				}
			}
		}

		public string FileName => string.IsNullOrEmpty(FilePath) || FilePath == "No things loaded" ? "" : System.IO.Path.GetFileName(FilePath);

		public ArchiveFormat ArchiveFormat => ArchiveFormatHelper.FromPath(FilePath);

		private Dictionary<string, int>? _catalogFlagUsageCounts;

		public ThingCatalog? Catalog => _catalog;
		public AssetsViewModel? ParentViewModel => _parentViewModel;

		public Dictionary<string, int> GetOrBuildFlagUsageCounts()
		{
			if (_catalogFlagUsageCounts != null) return _catalogFlagUsageCounts;
			_catalogFlagUsageCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

			if (_catalog != null)
			{
				foreach (var t in _catalog.EnumerateItems()) ScanThing(t);
				foreach (var t in _catalog.EnumerateOutfits()) ScanThing(t);
				foreach (var t in _catalog.EnumerateEffects()) ScanThing(t);
				foreach (var t in _catalog.EnumerateMissiles()) ScanThing(t);
			}

			return _catalogFlagUsageCounts;

			void ScanThing(ThingType t)
			{
				foreach (var key in t.ExtraProperties.Keys)
				{
					_catalogFlagUsageCounts.TryGetValue(key, out var cur);
					_catalogFlagUsageCounts[key] = cur + 1;
				}
			}
		}

		public void InvalidateFlagUsageCountsCache() => _catalogFlagUsageCounts = null;

		public ClientDataReadOptions GetWriteOptions() => new ClientDataReadOptions
		{
			ClientVersion = new ClientDataVersion { Value = _clientVersion },
			ExtendedSpriteIds = UseExtendedThingIds,
			ImprovedAnimations = UseFrameAnimations,
			OutfitFrameGroups = UseFrameGroups,
			TransparentSprites = SettingsViewModel.UseTransparentPixels,
			CustomFlagMap = FloatingThingEditorViewModel.GetCustomFlagWriteMap(_clientVersion)
		};

		public FloatingSpriteLoaderViewModel? LinkedSpritePanel { get; set; }

		public bool GuessSettingsFromSignature
		{
			get => _guessSettingsFromSignature;
			set
			{
				if (SetProperty(ref _guessSettingsFromSignature, value))
				{
					OnPropertyChanged(nameof(CanEditManualSettings));
					if (value && PreferOtfiSettings) PreferOtfiSettings = false;
				}
			}
		}

		public bool PreferOtfiSettings
		{
			get => _preferOtfiSettings;
			set
			{
				if (SetProperty(ref _preferOtfiSettings, value))
				{
					OnPropertyChanged(nameof(CanEditManualSettings));
					if (value && GuessSettingsFromSignature) GuessSettingsFromSignature = false;
				}
			}
		}

		public bool CanEditManualSettings => !GuessSettingsFromSignature && !PreferOtfiSettings;

		public bool UseExtendedThingIds
		{
			get => _useExtendedThingIds;
			set => SetProperty(ref _useExtendedThingIds, value);
		}

		public bool UseFrameAnimations
		{
			get => _useFrameAnimations;
			set => SetProperty(ref _useFrameAnimations, value);
		}

		public bool UseFrameGroups
		{
			get => _useFrameGroups;
			set => SetProperty(ref _useFrameGroups, value);
		}

		public uint TotalThings
		{
			get => _totalThings;
			private set
			{
				if (SetProperty(ref _totalThings, value))
				{
					OnPropertyChanged(nameof(TotalPages));
					OnPropertyChanged(nameof(HasNextPage));
					OnPropertyChanged(nameof(HasPreviousPage));
					OnPropertyChanged(nameof(IsSectionEmpty));
					OnPropertyChanged(nameof(ShowThingList));
					OnPropertyChanged(nameof(ShowListViewContent));
					OnPropertyChanged(nameof(ShowGridViewContent));
					ImportThingCommand.NotifyCanExecuteChanged();
					GoToIdCommand.NotifyCanExecuteChanged();
					NewThingCommand.NotifyCanExecuteChanged();
				}
			}
		}

		public bool IsArchiveLoaded => _catalog != null;

		private string? _errorMessage;
		public string? ErrorMessage
		{
			get => _errorMessage;
			set
			{
				if (SetProperty(ref _errorMessage, value))
				{
					OnPropertyChanged(nameof(HasError));
					OnPropertyChanged(nameof(ShowSpritesNotLoadedWarning));
					OnPropertyChanged(nameof(ShowLoadThingsDropzone));
				}
			}
		}

		public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

		[RelayCommand]
		public void DismissError()
		{
			ErrorMessage = null;
		}

		[RelayCommand]
		private void ToggleHideEmpty() => HideEmpty = !HideEmpty;

		[RelayCommand]
		private void ToggleAnimateAll() => AnimateAll = !AnimateAll;

		private Avalonia.Threading.DispatcherTimer? _animateAllTimer;

		private void StartAnimateAllTimer()
		{
			_animateAllTimer?.Stop();
			_animateAllTimer = new Avalonia.Threading.DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(150)
			};
			_animateAllTimer.Tick += OnAnimateAllTimerTick;
			_animateAllTimer.Start();
		}

		private void StopAnimateAllTimer()
		{
			if (_animateAllTimer != null)
			{
				_animateAllTimer.Tick -= OnAnimateAllTimerTick;
				_animateAllTimer.Stop();
				_animateAllTimer = null;
			}
		}

		private void OnAnimateAllTimerTick(object? sender, EventArgs e)
		{
			if (!AnimateAll)
			{
				StopAnimateAllTimer();
				return;
			}

			foreach (var item in PagedThings)
			{
				item.StepAnimation();
			}
		}

		private bool _isGridView = true;

		public bool IsGridView
		{
			get => _isGridView;
			set
			{
				if (SetProperty(ref _isGridView, value))
				{
					OnPropertyChanged(nameof(IsListView));
					OnPropertyChanged(nameof(ShowListViewContent));
					OnPropertyChanged(nameof(ShowGridViewContent));
				}
			}
		}

		public bool IsListView => !_isGridView;

		[RelayCommand]
		private void ToggleViewMode() => IsGridView = !IsGridView;

		public int CurrentPage
		{
			get => _currentPage;
			set
			{
				if (value < 1) value = 1;
				int maxPage = TotalPages;
				if (value > maxPage && maxPage > 0) value = maxPage;

				if (SetProperty(ref _currentPage, value))
				{
					ClearSelection();
					UpdatePage();
					OnPropertyChanged(nameof(HasNextPage));
					OnPropertyChanged(nameof(HasPreviousPage));
				}
			}
		}

		public int PageSize
		{
			get => _pageSize;
			set
			{
				if (SetProperty(ref _pageSize, value))
				{
					OnPropertyChanged(nameof(TotalPages));
					CurrentPage = 1;
					UpdatePage();
				}
			}
		}

		public int TotalPages => TotalThings == 0 ? 0 : (int)((TotalThings + PageSize - 1) / PageSize);

		public bool HasPreviousPage => CurrentPage > 1;
		public bool HasNextPage => CurrentPage < TotalPages;

		public string JumpToIdText
		{
			get => _jumpToIdText;
			set
			{
				if (SetProperty(ref _jumpToIdText, value))
					GoToIdCommand.NotifyCanExecuteChanged();
			}
		}

		public int[] AvailablePageSizes { get; } = { 25, 50, 100, 200, 500, 1000 };

		public bool HasThingSelection => GetSelectedThings().Count > 0;
		public int SelectedThingCount => GetSelectedThings().Count;
		public bool HasMultipleThingSelection => SelectedThingCount > 1;

		public int AssetDisplaySize => SettingsViewModel.AssetDisplaySize;
		public int ListBorderWidthHeight => AssetDisplaySize + 4;
		public int GridTileWidth => AssetDisplaySize + 40;
		public int GridTileHeight => AssetDisplaySize + 44;

		public readonly HashSet<uint> AddedThingIds = new HashSet<uint>();
		public readonly HashSet<uint> RemovedThingIds = new HashSet<uint>();
		public readonly HashSet<uint> ModifiedThingIds = new HashSet<uint>();

		public void DiscardChanges()
		{
			if (!string.IsNullOrEmpty(FilePath) && FilePath != "No things loaded")
			{
				LoadArchive(FilePath);
				HasSavedChanges = false;
			}
		}

		public FloatingThingsLoaderViewModel(AssetsViewModel? parentViewModel = null)
		{
			_parentViewModel = parentViewModel;
			PanelWidth = SettingsViewModel.DefaultThingsPanelWidth;
			ContentHeight = SettingsViewModel.DefaultThingsPanelHeight;
			SettingsViewModel.ThingIdOffsetChanged += OnThingIdOffsetChanged;
			SettingsViewModel.ClientVersionChanged += OnClientVersionChanged;
			SettingsViewModel.AssetDisplaySizeChanged += OnAssetDisplaySizeChanged;
			ResetSettingsToDefaults();
			_undoRedoStack = new Services.Archive.UndoRedoStack<Services.Archive.ThingUndoAction>(SettingsViewModel.UndoLimit);
		}

		private void OnThingIdOffsetChanged(uint newOffset)
		{
			foreach (var item in PagedThings)
				item.NotifyDisplayedIdChanged();
			CatalogChanged?.Invoke();
		}

		private void OnClientVersionChanged(uint newVersion)
		{
			if (IsArchiveLoaded)
				return;
			_clientVersion = newVersion;
			if (GuessSettingsFromSignature && !PreferOtfiSettings)
				ResetSettingsToDefaults();
		}

		public void ResetSettingsToDefaults()
		{
			var version = new ClientDataVersion { Value = _clientVersion };
			UseExtendedThingIds = DatThingFormatRules.UsesExtendedSpriteIdsByDefault(version);
			UseFrameAnimations = DatThingFormatRules.UsesImprovedAnimationsByDefault(version);
			UseFrameGroups = DatThingFormatRules.UsesOutfitFrameGroupsByDefault(version);
		}

		public void Dispose()
		{
			StopAnimateAllTimer();
			SettingsViewModel.ThingIdOffsetChanged -= OnThingIdOffsetChanged;
			SettingsViewModel.ClientVersionChanged -= OnClientVersionChanged;
			SettingsViewModel.AssetDisplaySizeChanged -= OnAssetDisplaySizeChanged;
			_undoRedoStack?.Clear();
		}

		private void OnAssetDisplaySizeChanged(int newSize)
		{
			OnPropertyChanged(nameof(AssetDisplaySize));
			OnPropertyChanged(nameof(ListBorderWidthHeight));
			OnPropertyChanged(nameof(GridTileWidth));
			OnPropertyChanged(nameof(GridTileHeight));
		}

		public SpriteLoader? GetActiveSpriteLoader()
		{
			var spritePanel = _parentViewModel?.ResolveSpritePanelFor(this);
			return spritePanel is { IsArchiveLoaded: true } ? spritePanel.Loader : null;
		}

		public ThingType? GetThingType(uint id)
		{
			var listed = _allThings.Find(t => t.Id == id);
			if (_catalog == null || listed == null)
				return listed;

			return listed.Kind switch
			{
				ThingKind.Item => _catalog.TryGetItem(id) ?? listed,
				ThingKind.Outfit => _catalog.TryGetOutfit(id) ?? listed,
				ThingKind.Effect => _catalog.TryGetEffect(id) ?? listed,
				ThingKind.Missile => _catalog.TryGetMissile(id) ?? listed,
				_ => listed,
			};
		}

		public IReadOnlyList<ThingType> EnumerateThings(ThingKind kind)
		{
			if (_catalog == null) return Array.Empty<ThingType>();
			return kind switch
			{
				ThingKind.Item => _catalog.EnumerateItems().ToList(),
				ThingKind.Outfit => _catalog.EnumerateOutfits().ToList(),
				ThingKind.Effect => _catalog.EnumerateEffects().ToList(),
				ThingKind.Missile => _catalog.EnumerateMissiles().ToList(),
				_ => Array.Empty<ThingType>(),
			};
		}

		public void SyncThingInList(ThingType thing, bool replaceExisting)
		{
			if (HideEmpty)
			{
				ReloadThingsForSection(preserveCurrentPage: true);
				return;
			}

			var idx = _allThings.FindIndex(t => t.Id == thing.Id);
			if (idx >= 0)
			{
				if (replaceExisting)
					_allThings[idx] = thing;
			}
			else if (thing.Kind == SelectedSection)
			{
				_allThings.Add(thing);
				_allThings.Sort((a, b) => a.Id.CompareTo(b.Id));
			}

			TotalThings = (uint)_allThings.Count;
		}

		public void RefreshAfterCatalogMutation(bool goToLastPage = false)
		{
			HasSavedChanges = true;

			if (goToLastPage)
			{
				var lastPage = TotalPages;
				if (CurrentPage != lastPage)
				{
					_currentPage = lastPage;
					OnPropertyChanged(nameof(CurrentPage));
					OnPropertyChanged(nameof(HasNextPage));
					OnPropertyChanged(nameof(HasPreviousPage));
					UpdatePage();
					return;
				}
			}

			UpdatePage();
			RefreshPreviews();
			NotifySelectionChanged();
		}

		public Avalonia.Media.Imaging.WriteableBitmap? GetPreviewForThing(ThingType thing, int frameIndex = 0)
		{
			var loader = GetActiveSpriteLoader();
			if (loader == null)
				return null;

			try
			{
				var preview = ThingPreviewRenderer.RenderPreview(thing, loader, frameIndex);
				return preview == null
					? null
					: _renderer.ConvertRgba(preview.Width, preview.Height, preview.Pixels);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[ThingsLoader] Preview failed for ThingID {thing.Id}: {ex.Message}");
				return null;
			}
		}

		public bool IsSpriteLoaderLoaded =>
			GetActiveSpriteLoader() != null
			|| _parentViewModel?.HasAnyPendingSpriteForThings() == true;

		public bool ShowSpritesNotLoadedWarning => !IsSpriteLoaderLoaded && !HasError;
		public bool ShowLoadThingsDropzone => IsSpriteLoaderLoaded && !HasError;

		public void NotifySpriteLinkChanged()
		{
			OnPropertyChanged(nameof(IsSpriteLoaderLoaded));
			OnPropertyChanged(nameof(ShowSpritesNotLoadedWarning));
			OnPropertyChanged(nameof(ShowLoadThingsDropzone));
			OnPropertyChanged(nameof(CanCompile));
			CompileCommand.NotifyCanExecuteChanged();
			RefreshPreviews();
		}

		public void RefreshPreviews()
		{
			OnPropertyChanged(nameof(IsSpriteLoaderLoaded));
			OnPropertyChanged(nameof(ShowSpritesNotLoadedWarning));
			OnPropertyChanged(nameof(ShowLoadThingsDropzone));
			foreach (var item in PagedThings)
				item.InvalidatePreview();
		}

		public void ApplyThingEdit(ThingType thing)
		{
			if (_catalog == null)
				return;

			StartThingTransaction(new[] { (thing.Kind, thing.Id) });

			switch (thing.Kind)
			{
				case ThingKind.Item:
					_catalog.PutItem(thing);
					break;
				case ThingKind.Outfit:
					_catalog.PutOutfit(thing);
					break;
				case ThingKind.Effect:
					_catalog.PutEffect(thing);
					break;
				case ThingKind.Missile:
					_catalog.PutMissile(thing);
					break;
			}

			if (!AddedThingIds.Contains(thing.Id))
			{
				ModifiedThingIds.Add(thing.Id);
			}

			SyncThingInList(thing, replaceExisting: true);
			PagedThings.FirstOrDefault(t => t.Id == thing.Id)?.InvalidatePreview();

			EndThingTransaction(new[] { (thing.Kind, thing.Id) });
		}

		public void ApplyThingEdits(IEnumerable<ThingType> things)
		{
			if (_catalog == null) return;
			var edits = things.ToList();
			if (edits.Count == 0) return;
			var affected = edits.Select(t => (t.Kind, t.Id)).ToList();
			StartThingTransaction(affected);

			foreach (var thing in edits)
			{
				switch (thing.Kind)
				{
					case ThingKind.Item: _catalog.PutItem(thing); break;
					case ThingKind.Outfit: _catalog.PutOutfit(thing); break;
					case ThingKind.Effect: _catalog.PutEffect(thing); break;
					case ThingKind.Missile: _catalog.PutMissile(thing); break;
				}
				if (!AddedThingIds.Contains(thing.Id)) ModifiedThingIds.Add(thing.Id);
				SyncThingInList(thing, replaceExisting: true);
				PagedThings.FirstOrDefault(t => t.Id == thing.Id)?.InvalidatePreview();
			}

			HasSavedChanges = true;
			EndThingTransaction(affected);
		}

		public uint GetNextThingId(ThingKind kind)
		{
			if (_catalog == null) throw new InvalidOperationException("A things archive must be loaded before importing.");
			return ThingExchangeHelper.GetNextAppendId(_catalog, kind);
		}

		public IReadOnlyList<uint> ImportSlicerThings(
			IReadOnlyList<byte[]> spritePixels,
			IReadOnlyList<ThingType> things,
			bool replaceExisting)
		{
			if (_catalog == null) throw new InvalidOperationException("A things archive must be loaded before importing.");
			if (things.Count == 0) return Array.Empty<uint>();
			var spritePanel = LinkedSpritePanel;
			if (spritePanel is not { IsArchiveLoaded: true })
				throw new InvalidOperationException("The selected things archive is not linked to a loaded sprite archive.");
			var kind = things[0].Kind;
			if (things.Any(t => t.Kind != kind)) throw new InvalidOperationException("A slicer batch must contain one thing kind.");

			if (replaceExisting)
			{
				if (things.Count != 1 || GetThingFromCatalog(kind, things[0].Id) == null)
					throw new InvalidOperationException("The replacement target no longer exists.");
			}
			else
			{
				var expected = GetNextThingId(kind);
				if ((ulong)expected + (ulong)things.Count - 1 > uint.MaxValue)
					throw new InvalidOperationException("The things archive does not have enough ID space for this import.");
				for (var i = 0; i < things.Count; i++)
					if (things[i].Id != checked((uint)((ulong)expected + (uint)i)))
						throw new InvalidOperationException("Thing storage changed during import. Review the target and try again.");
			}

			var affected = things.Select(t => (t.Kind, t.Id)).ToList();
			StartThingTransaction(affected);
			var thingCheckpoint = _currentAction ?? throw new InvalidOperationException("Could not start the thing import transaction.");
			FloatingSpriteLoaderViewModel.SlicerAppendCheckpoint? spriteCheckpoint = null;
			try
			{
				spriteCheckpoint = spritePanel.BeginSlicerAppend(spritePixels);
				foreach (var thing in things)
				{
					PutThingIntoCatalog(thing.Kind, thing);
					if (replaceExisting)
					{
						if (!AddedThingIds.Contains(thing.Id)) ModifiedThingIds.Add(thing.Id);
					}
					else AddedThingIds.Add(thing.Id);
				}

				HasSavedChanges = true;
				if (SelectedSection != kind) SelectedSection = kind;
				else ReloadThingsForSection(preserveCurrentPage: replaceExisting, goToLastPage: !replaceExisting);
				spritePanel.CommitSlicerAppend(spriteCheckpoint);
				EndThingTransaction(affected);
				return things.Select(t => t.Id).ToList();
			}
			catch
			{
				if (spriteCheckpoint != null) spritePanel.RollbackSlicerAppend(spriteCheckpoint);
				RevertCounts(thingCheckpoint.ItemCountBefore, thingCheckpoint.OutfitCountBefore, thingCheckpoint.EffectCountBefore, thingCheckpoint.MissileCountBefore);
				foreach (var entry in thingCheckpoint.ThingsBefore)
				foreach (var pair in entry.Value)
					PutThingIntoCatalog(entry.Key, pair.Value);
				AddedThingIds.Clear(); foreach (var id in thingCheckpoint.AddedBefore) AddedThingIds.Add(id);
				RemovedThingIds.Clear(); foreach (var id in thingCheckpoint.RemovedBefore) RemovedThingIds.Add(id);
				ModifiedThingIds.Clear(); foreach (var id in thingCheckpoint.ModifiedBefore) ModifiedThingIds.Add(id);
				HasSavedChanges = thingCheckpoint.HasSavedChangesBefore;
				_currentAction = null;
				_undoRedoStack?.DiscardLatestUndoIfMatches(thingCheckpoint);
				ReloadThingsForSection();
				RefreshUndoRedoCommands();
				throw;
			}
		}

		private bool _hasSavedChanges;
		public bool HasSavedChanges
		{
			get => _hasSavedChanges;
			set
			{
				if (SetProperty(ref _hasSavedChanges, value))
				{
					_parentViewModel?.RefreshCompileCommands();
					CompileCommand.NotifyCanExecuteChanged();
				}
			}
		}

		public bool CanCompile => IsArchiveLoaded && LinkedSpritePanel != null && HasSavedChanges;

		public event EventHandler<string>? RequestShowInfo;
		public event EventHandler<(string Title, string Message, string? InfoMessage, string? SnippetCode)>? RequestShowWarning;

		[RelayCommand]
		private void ShowInfo()
		{
			if (!IsArchiveLoaded) return;
			RequestShowInfo?.Invoke(this, BuildArchiveInfoText());
		}

		public string BuildArchiveInfoText()
		{
			var path = string.IsNullOrWhiteSpace(FilePath) || FilePath == "No things loaded"
				? "(unsaved / no path)"
				: FilePath;
			var format = ArchiveFormat switch
			{
				ArchiveFormat.Dat => SupportedFileFormats.ExtDat,
				ArchiveFormat.Things => SupportedFileFormats.ExtJson,
				_ => ArchiveFormat.ToString()
			};
			var settingsMode = PreferOtfiSettings
				? "Prefer OTFI"
				: GuessSettingsFromSignature
					? "Guess from signature"
					: "Manual";

			var itemCount = _catalog?.ItemCount ?? 0;
			var outfitCount = _catalog?.OutfitCount ?? 0;
			var effectCount = _catalog?.EffectCount ?? 0;
			var missileCount = _catalog?.MissileCount ?? 0;

			var lines = new List<string>
			{
				$"Path: {path}",
				$"Format: {format}",
				$"Total things: {itemCount + outfitCount + effectCount + missileCount}",
				$"  Items: {itemCount}",
				$"  Outfits: {outfitCount}",
				$"  Effects: {effectCount}",
				$"  Missiles: {missileCount}",
			};

			if (ArchiveFormat == ArchiveFormat.Dat)
			{
				lines.Add($"DAT signature: 0x{_catalog?.DatSignature ?? 0:X8}");
				lines.Add($"Client version: {SettingsViewModel.ClientVersion}");
				lines.Add($"Extended sprite IDs: {(UseExtendedThingIds ? "Yes" : "No")}");
				lines.Add($"Frame animations: {(UseFrameAnimations ? "Yes" : "No")}");
				lines.Add($"Frame groups: {(UseFrameGroups ? "Yes" : "No")}");
				lines.Add($"Settings mode: {settingsMode}");
			}

			return string.Join(Environment.NewLine, lines);
		}

		[RelayCommand(CanExecute = nameof(CanCompile))]
		private async System.Threading.Tasks.Task Compile()
		{
			if (LinkedSpritePanel == null) return;
			try
			{
				bool compileSprites = NyxAssetsEditor.ViewModels.Pages.SettingsViewModel.CompileLinkedPairTogether && LinkedSpritePanel.HasSavedChanges;

				var (savedPage, savedId) = SaveViewState();

				if (compileSprites)
				{
					ArchiveCompileService.BackupIfExists(LinkedSpritePanel.FilePath);
					ArchiveCompileService.BackupIfExists(FilePath);

					ArchiveCompileService.CompilePair(
						LinkedSpritePanel,
						this,
						LinkedSpritePanel.FilePath,
						FilePath);

					await LinkedSpritePanel.LoadArchiveAsync(LinkedSpritePanel.FilePath);
					LinkedSpritePanel.HasSavedChanges = false;
					
					await LoadArchiveAsync(FilePath, useLastLoadedSprite: false);
					HasSavedChanges = false;
				}
				else
				{
					ArchiveCompileService.BackupIfExists(FilePath);
					
					var options = GetWriteOptions();
					var format = ArchiveFormat;
					if (Catalog != null)
					{
						if (format == ArchiveFormat.Dat)
						{
							using var datStream = File.Create(FilePath);
							Catalog.WriteDatTo(datStream, options);
						}
						else
						{
							Catalog.ExportJson(FilePath, options);
						}
					}

					await LoadArchiveAsync(FilePath, useLastLoadedSprite: false);
					HasSavedChanges = false;
				}
				RestoreViewState(savedPage, savedId);
				_parentViewModel?.RefreshCompileCommands();
			}
			catch (Exception ex)
			{
				ErrorMessage = $"Compile failed: {ex.Message}";
			}
		}

		private (int page, uint thingId) SaveViewState() =>
			(_currentPage, SelectedThing?.Id ?? 0);

		private void RestoreViewState(int page, uint thingId)
		{
			int maxPage = TotalPages;
			int target = Math.Min(page, maxPage > 0 ? maxPage : 1);
			if (_currentPage != target)
			{
				_currentPage = target;
				OnPropertyChanged(nameof(CurrentPage));
				OnPropertyChanged(nameof(HasNextPage));
				OnPropertyChanged(nameof(HasPreviousPage));
				UpdatePage();
			}
			if (thingId != 0)
			{
				var item = PagedThings.FirstOrDefault(t => t.Id == thingId);
				if (item != null)
					SelectThing(item);
			}
		}

		public FloatingThingEditorViewModel? GetActiveEditor() =>
			_parentViewModel?.ActivePanels.OfType<FloatingThingEditorViewModel>()
				.FirstOrDefault(p => ReferenceEquals(p.SourcePanel, this));

		public async System.Threading.Tasks.Task<bool> RequestSelectThing(ThingItemViewModel thing, bool shift = false, bool ctrl = false)
		{
			var editor = GetActiveEditor();
			if (editor != null && editor.IsDirty && editor.ThingId != thing.Id)
			{
				var tcs = new System.Threading.Tasks.TaskCompletionSource<FloatingThingEditorViewModel.PromptResult>();
				editor.ShowPrompt(
					"Save Changes?",
					$"Save changes done to thing {editor.ThingId}?",
					tcs);
				var result = await tcs.Task;
				if (result == FloatingThingEditorViewModel.PromptResult.Save)
				{
					editor.Save();
				}
				else if (result == FloatingThingEditorViewModel.PromptResult.Cancel)
				{
					return false;
				}
			}
			SelectThing(thing, shift, ctrl);
			return true;
		}

		public System.Threading.Tasks.Task OpenThingEditor(ThingItemViewModel item, bool newWindow = false) =>
			_parentViewModel != null
				? _parentViewModel.OpenThingEditor(this, item.Id, newWindow)
				: System.Threading.Tasks.Task.CompletedTask;

		public void OpenMultiThingEditor(IEnumerable<ThingItemViewModel> items) =>
			_parentViewModel?.OpenMultiThingEditor(this, items.Select(i => i.Id));

		private IEnumerable<ThingType> EnumerateSelectedSection()
		{
			if (_catalog == null)
				yield break;

			foreach (var thing in SelectedSection switch
			{
				ThingKind.Item => _catalog.EnumerateItems(),
				ThingKind.Outfit => _catalog.EnumerateOutfits(),
				ThingKind.Effect => _catalog.EnumerateEffects(),
				ThingKind.Missile => _catalog.EnumerateMissiles(),
				_ => Enumerable.Empty<ThingType>(),
			})
				yield return thing;
		}

		private bool HasSpritesAssigned(ThingType thing)
		{
			if (thing.FrameGroups == null || thing.FrameGroups.Count == 0)
				return false;

			foreach (var fg in thing.FrameGroups)
			{
				if (fg.SpriteIds != null && fg.SpriteIds.Any(id => id != 0))
					return true;
			}

			return false;
		}

		private void ReloadThingsForSection(bool preserveCurrentPage = false, bool goToLastPage = false)
		{
			_allThings.Clear();
			var items = EnumerateSelectedSection().ToList();
			int lastNonEmptyIdx = -1;
			for (int i = 0; i < items.Count; i++)
			{
				if (HasSpritesAssigned(items[i]))
				{
					lastNonEmptyIdx = i;
				}
			}

			for (int i = 0; i < items.Count; i++)
			{
				var thing = items[i];
				bool isAtEnd = (items.Count - i <= 20) || (i > lastNonEmptyIdx);
				if (HideEmpty && !isAtEnd && !HasSpritesAssigned(thing))
					continue;
				_allThings.Add(thing);
			}

			TotalThings = (uint)_allThings.Count;
			_selectionAnchor = null;
			SelectedThing = null;
			if (goToLastPage)
			{
				_currentPage = TotalPages;
			}
			else if (!preserveCurrentPage)
			{
				_currentPage = 1;
			}
			else if (_currentPage > TotalPages && TotalPages > 0)
			{
				_currentPage = TotalPages;
			}
			OnPropertyChanged(nameof(CurrentPage));
			OnPropertyChanged(nameof(HasNextPage));
			OnPropertyChanged(nameof(HasPreviousPage));
			UpdatePage();
			NotifySelectionChanged();
			CatalogChanged?.Invoke();
		}

		public async Task CreateNewArchiveAsync(string format, uint clientVersion, bool useExtendedThingIds, bool useFrameAnimations, bool useFrameGroups)
		{
			AddedThingIds.Clear();
			RemovedThingIds.Clear();
			ModifiedThingIds.Clear();

			_clientVersion = clientVersion;
			UseExtendedThingIds = useExtendedThingIds;
			UseFrameAnimations = useFrameAnimations;
			UseFrameGroups = useFrameGroups;

			FilePath = format.ToLower() == "dat"
				? "Untitled" + SupportedFileFormats.ExtDat
				: "Untitled.things";

			var datFormat = clientVersion switch
			{
				740 => NyxAssets.Things.DatThingFormat.V2_7_40__7_50,
				760 => NyxAssets.Things.DatThingFormat.V3_7_55__7_72,
				860 => NyxAssets.Things.DatThingFormat.V5_8_60__9_86,
				_ => NyxAssets.Things.DatThingFormat.V6_10_10__10_56
			};

			var versionEntry = ClientVersion.AvailableVersions.Find(v => v.Version == clientVersion);
			var catalog = new ThingCatalog();
			catalog.DatSignature = versionEntry?.DatSignature ?? 0U;
			catalog.DatFormat = datFormat;
			_catalog = catalog;

			_selectedSection = ThingKind.Item;
			NotifySectionProperties();
			OnPropertyChanged(nameof(IsArchiveLoaded));
			ReloadThingsForSection();
			HasSavedChanges = true;
		}

		public void LoadArchive(string path, bool useLastLoadedSprite = true) =>
			_ = LoadArchiveAsync(path, useLastLoadedSprite);

		public async Task LoadArchiveAsync(string path, bool useLastLoadedSprite = true)
		{
			_undoRedoStack?.Clear();
			RefreshUndoRedoCommands();

			ErrorMessage = null;

			if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
			{
				FilePath = string.IsNullOrWhiteSpace(path) ? "No things loaded" : path;
				ErrorMessage = string.IsNullOrWhiteSpace(path)
					? "No things path was provided."
					: $"Could not find file:\n{path}";
				_catalog = null;
				OnPropertyChanged(nameof(IsArchiveLoaded));
				CatalogChanged?.Invoke();
				return;
			}

			if (PreferOtfiSettings && SupportedFileFormats.HasExtension(path, SupportedFileFormats.ExtDat))
			{
				var otfi = OtfiSettingsReader.ReadForArchive(path, out var warning);
				var missing = new List<string>();
				if (otfi != null && otfi.Extended == null) missing.Add("extended");
				if (otfi != null && otfi.FrameDurations == null) missing.Add("frame-durations");
				if (otfi != null && otfi.FrameGroups == null) missing.Add("frame-groups");
				if (otfi == null || missing.Count > 0)
				{
					PreferOtfiSettings = false;
					GuessSettingsFromSignature = true;
					ResetSettingsToDefaults();
					var reason = warning ?? $"The OTFI file is missing {string.Join(", ", missing)}.";
					ErrorMessage = $"OTFI settings could not be used. {reason} Reverted to recommended settings.";
				}
				else
				{
					UseExtendedThingIds = otfi.Extended.GetValueOrDefault();
					UseFrameAnimations = otfi.FrameDurations.GetValueOrDefault();
					UseFrameGroups = otfi.FrameGroups.GetValueOrDefault();
				}
			}

			if (SupportedFileFormats.HasExtension(path, SupportedFileFormats.ExtDat) && System.IO.File.Exists(path))
			{
				uint signature = 0;
				try
				{
					using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
					using (var br = new System.IO.BinaryReader(fs))
					{
						if (fs.Length >= 4)
							signature = br.ReadUInt32();
					}
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"Failed to read dat signature: {ex.Message}");
				}

				if (signature != 0)
				{
					var versionEntry = ClientVersion.AvailableVersions.Find(v => v.DatSignature == signature);
					if (versionEntry == null)
					{
						if (!SettingsViewModel.AllowUnknownSignatures)
						{
							string hexSig = signature.ToString("X8").TrimStart('0');
							if (string.IsNullOrEmpty(hexSig)) hexSig = "0";

							string sprSigHex = "0";
							var spritePanel = LinkedSpritePanel ?? _parentViewModel?.ResolveSpritePanelFor(this);
							if (spritePanel?.Loader != null)
							{
								sprSigHex = spritePanel.Loader.SprSignature.ToString("X8").TrimStart('0');
								if (string.IsNullOrEmpty(sprSigHex)) sprSigHex = "0";
							}

							ErrorMessage = $"Unsupported version\nSignature: 0x{signature:X8}";
							_catalog = null;
							OnPropertyChanged(nameof(IsArchiveLoaded));
							CatalogChanged?.Invoke();

							string snippet = $"[[versions]]\nvalue = YOUR_VERSION\nstring = \"YOUR_VERSION\"\ndat = \"{hexSig}\"\nspr = \"{sprSigHex}\"\notb = 0";
							string infoBoxMessage = $"Detected custom signature 0x{signature:X8} which is not present in signatures.toml.\n\nTo fix this, add the snippet below to signatures.toml (or enable 'Allow unknown signatures' in Settings).";
							RequestShowWarning?.Invoke(this, ("Unsupported .dat Signature", $"The file signature 0x{signature:X8} does not match any known client version.", infoBoxMessage, snippet));
							return;
						}
					}
					else
					{
						_clientVersion = versionEntry.Version;
						if (GuessSettingsFromSignature && !PreferOtfiSettings)
							ResetSettingsToDefaults();
						SettingsViewModel.ClientVersion = versionEntry.Version;
					}
				}
			}

			AddedThingIds.Clear();
			RemovedThingIds.Clear();
			ModifiedThingIds.Clear();

			var thingsFormat = ArchiveFormatHelper.FromPath(path);
			var isNewArchive = !IsArchiveLoaded
				|| string.IsNullOrEmpty(FilePath)
				|| FilePath == "No things loaded"
				|| !string.Equals(path, FilePath, StringComparison.OrdinalIgnoreCase);

			if (useLastLoadedSprite && isNewArchive)
			{
				if (_parentViewModel?.TryAssignPendingSpriteLink(this, thingsFormat) != true)
					return;
			}
			else if (isNewArchive && _parentViewModel?.ResolveSpritePanelFor(this) is not { IsArchiveLoaded: true })
				return;

			FilePath = path;
			try
			{
				var options = GetWriteOptions();
				_catalog = await Task.Run(() => ReadCatalogFromFile(path, options)).ConfigureAwait(true);

				_selectedSection = ThingKind.Item;
				NotifySectionProperties();
				OnPropertyChanged(nameof(IsArchiveLoaded));
				ReloadThingsForSection();
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[ThingsLoader] FAILED TO LOAD DAT/THINGS: {ex}");
				Debug.WriteLine($"Failed to load catalog: {ex.Message}");
				_catalog = null;
				ErrorMessage = $"Failed to load things:\n{ex.Message}";
				OnPropertyChanged(nameof(IsArchiveLoaded));
				CatalogChanged?.Invoke();
				_allThings.Clear();
				TotalThings = 0;

				string? infoBoxMessage = null;
				string? snippet = null;
				if (SupportedFileFormats.HasExtension(path, SupportedFileFormats.ExtDat) && System.IO.File.Exists(path))
				{
					uint datSignature = 0;
					try
					{
						using var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read);
						using var br = new System.IO.BinaryReader(fs);
						if (fs.Length >= 4)
							datSignature = br.ReadUInt32();
					}
					catch { }

					if (datSignature != 0)
					{
						string hexSig = datSignature.ToString("X8").TrimStart('0');
						if (string.IsNullOrEmpty(hexSig)) hexSig = "0";

						string sprSigHex = "0";
						var spritePanel = LinkedSpritePanel ?? _parentViewModel?.ResolveSpritePanelFor(this);
						if (spritePanel?.Loader != null)
						{
							sprSigHex = spritePanel.Loader.SprSignature.ToString("X8").TrimStart('0');
							if (string.IsNullOrEmpty(sprSigHex)) sprSigHex = "0";
						}

						var known = ClientVersion.AvailableVersions.Find(v => v.DatSignature == datSignature);
						if (known == null)
						{
							snippet = $"[[versions]]\nvalue = YOUR_VERSION\nstring = \"YOUR_VERSION\"\ndat = \"{hexSig}\"\nspr = \"{sprSigHex}\"\notb = 0";
							infoBoxMessage = $"Detected custom signature 0x{datSignature:X8} which is not present in signatures.toml.\n\nTo fix this, add the snippet below to signatures.toml (or enable 'Allow unknown signatures' in Settings).";
						}
					}
				}

				RequestShowWarning?.Invoke(this, ("Failed to Load .dat", ex.Message, infoBoxMessage, snippet));
			}

			_selectionAnchor = null;
			SelectedThing = null;
			if (_currentPage != 1)
				CurrentPage = 1;
			else
				UpdatePage();

			if (_catalog != null)
			{
				string spritePath = LinkedSpritePanel?.FilePath ?? "";
				if (spritePath == "No archive loaded") spritePath = "";
				string thingsPath = FilePath ?? "";
				if (thingsPath == "No things loaded") thingsPath = "";

				if (!string.IsNullOrEmpty(thingsPath) || !string.IsNullOrEmpty(spritePath))
				{
					NyxAssetsEditor.Services.Persistence.PersistenceService.AddRecentCombination(
						spritePath,
						thingsPath,
						spriteGuess: LinkedSpritePanel?.GuessSettingsFromSignature ?? true,
						spritePreferOtfi: LinkedSpritePanel?.PreferOtfiSettings ?? false,
						spriteTransparent: LinkedSpritePanel?.UseTransparentPixels ?? true,
						spriteExtended: LinkedSpritePanel?.UseExtendedSpriteIds ?? true,
						thingsGuess: GuessSettingsFromSignature,
						thingsPreferOtfi: PreferOtfiSettings,
						thingsExtended: UseExtendedThingIds,
						thingsAnimations: UseFrameAnimations,
						thingsGroups: UseFrameGroups
					);
				}
			}
			CatalogChanged?.Invoke();
		}

		private static ThingCatalog ReadCatalogFromFile(string path, ClientDataReadOptions options)
		{
			byte[] bytes = System.IO.File.ReadAllBytes(path);
			if (SupportedFileFormats.HasExtension(path, SupportedFileFormats.ExtDat))
				return new DatThingCatalogReader().Read(bytes, options);

			return new JsonThingCatalogReader().Read(bytes, options);
		}

		private void UpdatePage()
		{
			PagedThings.Clear();
			if (TotalThings == 0) return;

			int startIdx = (CurrentPage - 1) * PageSize;
			int endIdx = Math.Min(CurrentPage * PageSize, _allThings.Count);

			for (int i = startIdx; i < endIdx; i++)
			{
				var thing = _allThings[i];
				PagedThings.Add(new ThingItemViewModel(thing.Id, this));
			}
		}

		public void SelectThing(ThingItemViewModel thing, bool shift = false, bool ctrl = false)
		{
			if (shift)
			{
				if (_selectionAnchor != null)
				{
					ClearSelection();
					var things = PagedThings.OrderBy(t => t.Id).ToList();
					var anchorIdx = things.FindIndex(t => t.Id == _selectionAnchor.Id);
					var clickIdx = things.FindIndex(t => t.Id == thing.Id);
					if (anchorIdx < 0)
						anchorIdx = clickIdx;
					if (clickIdx >= 0)
					{
						var start = Math.Min(anchorIdx, clickIdx);
						var end = Math.Max(anchorIdx, clickIdx);
						for (var i = start; i <= end; i++)
							SetThingSelected(things[i], true);
					}
				}
				else
				{
					ClearSelection();
					SetThingSelected(thing, true);
					_selectionAnchor = thing;
				}
			}
			else if (ctrl)
			{
				SetThingSelected(thing, !thing.IsSelected);
				_selectionAnchor = thing;
			}
			else
			{
				ClearSelection();
				SetThingSelected(thing, true);
				_selectionAnchor = thing;
			}

			SelectedThing = thing;
			NotifySelectionChanged();
		}

		public IReadOnlyList<ThingItemViewModel> GetSelectedThings() =>
			PagedThings.Where(t => t.IsSelected).OrderBy(t => t.Id).ToList();

		private void ClearSelection()
		{
			foreach (var thing in PagedThings)
				thing.IsSelected = false;
		}

		private static void SetThingSelected(ThingItemViewModel thing, bool selected) =>
			thing.IsSelected = selected;

		private void NotifySelectionChanged()
		{
			OnPropertyChanged(nameof(HasThingSelection));
			OnPropertyChanged(nameof(SelectedThingCount));
			OnPropertyChanged(nameof(HasMultipleThingSelection));
			foreach (var item in PagedThings)
				item.NotifySelectionContextChanged();
			ImportThingCommand.NotifyCanExecuteChanged();
			ExportSelectedPngCommand.NotifyCanExecuteChanged();
			ExportSelectedJpegCommand.NotifyCanExecuteChanged();
			ExportSelectedBmpCommand.NotifyCanExecuteChanged();
			ExportSelectedNyxThingCommand.NotifyCanExecuteChanged();
			ExportSelectedObdCommand.NotifyCanExecuteChanged();
			ExportSelectedThingsCommand.NotifyCanExecuteChanged();
			DuplicateSelectedThingsCommand.NotifyCanExecuteChanged();
			RemoveSelectedThingsCommand.NotifyCanExecuteChanged();
			EditSelectedThingsCommand.NotifyCanExecuteChanged();
			ReplaceSelectedThingsCommand.NotifyCanExecuteChanged();
		}

		public void RequestReplaceThing(ThingItemViewModel thing) =>
			RequestReplaceThings(new[] { thing });

		public void RequestReplaceThings(IEnumerable<ThingItemViewModel> things)
		{
			var list = things.OrderBy(thing => thing.Id).ToList();
			if (list.Count == 0 || _catalog == null)
				return;
			if (list.Count > 1)
			{
				_parentViewModel?.OpenReplacerForThings(this, SelectedSection, list[0].Id, list[^1].Id);
				return;
			}

			RequestThingFileDialog?.Invoke(this, new ThingFileRequestEventArgs(list, "replace"));
		}

		public void RequestExportThing(ThingItemViewModel thing, string format) =>
			RequestExportThings(new[] { thing }, format);

		public void RequestExportThings(IEnumerable<ThingItemViewModel> things, string format)
		{
			var list = things.ToList();
			if (list.Count == 0)
				return;

			RequestThingFileDialog?.Invoke(this, new ThingFileRequestEventArgs(list, format));
		}

		public void RequestExportThing(ThingItemViewModel thing) =>
			RequestExportThings(new[] { thing });

		public void RequestExportThings(IEnumerable<ThingItemViewModel> things)
		{
			var list = things.ToList();
			if (list.Count == 0)
				return;

			RequestThingFileDialog?.Invoke(this, new ThingFileRequestEventArgs(list, "export_popup"));
		}

		public void RequestImportNewThing()
		{
			if (_catalog == null)
				return;

			RequestThingFileDialog?.Invoke(this, new ThingFileRequestEventArgs(null, "import"));
		}

		public void ApplyImportedDocument(ThingDocument document, uint assignId, bool replaceExisting)
		{
			if (_catalog == null)
				return;

			var kind = document.Thing.Kind;
			StartThingTransaction(new[] { (kind, assignId) });

			var loader = GetActiveSpriteLoader();
			try
			{
				ThingExchangeHelper.ImportDocument(document, _catalog, assignId, loader);
				var thing = ThingExchangeHelper.GetThingFromCatalog(_catalog, document.Thing.Kind, assignId);
				if (thing != null)
				{
					if (replaceExisting)
					{
						if (!AddedThingIds.Contains(assignId))
							ModifiedThingIds.Add(assignId);
					}
					else
					{
						AddedThingIds.Add(assignId);
					}

					if (thing.Kind != SelectedSection)
						SelectedSection = thing.Kind;
					else
						SyncThingInList(thing, replaceExisting);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[ThingsLoader] Import failed for id {assignId}: {ex.Message}");
				_currentAction = null;
				return;
			}

			RefreshAfterCatalogMutation(goToLastPage: !replaceExisting);

			EndThingTransaction(new[] { (kind, assignId) });
		}

		public void DuplicateThing(ThingItemViewModel thing) => DuplicateThings(new[] { thing });

		public void DuplicateThings(IEnumerable<ThingItemViewModel> things)
		{
			if (_catalog == null)
				return;

			var loader = GetActiveSpriteLoader();
			if (loader == null)
			{
				Debug.WriteLine("[ThingsLoader] Duplicate requires a loaded sprite archive.");
				return;
			}

			var itemsList = things.OrderBy(t => t.Id).ToList();
			var createdThings = new List<(ThingKind, uint)>();
			
			StartThingTransaction(Enumerable.Empty<(ThingKind, uint)>());

			foreach (var item in itemsList)
			{
				var source = GetThingType(item.Id);
				if (source == null)
					continue;

				try
				{
					var newId = ThingExchangeHelper.GetNextAppendId(_catalog, source.Kind);
					var clone = ThingCloner.Clone(source, newId);
					switch (source.Kind)
					{
						case ThingKind.Item:
							_catalog.PutItem(clone);
							break;
						case ThingKind.Outfit:
							_catalog.PutOutfit(clone);
							break;
						case ThingKind.Effect:
							_catalog.PutEffect(clone);
							break;
						case ThingKind.Missile:
							_catalog.PutMissile(clone);
							break;
					}

					AddedThingIds.Add(newId);
					createdThings.Add((source.Kind, newId));

					_allThings.Add(clone);
					_allThings.Sort((a, b) => a.Id.CompareTo(b.Id));
				}
				catch (Exception ex)
				{
					Debug.WriteLine($"[ThingsLoader] Failed to duplicate thing {item.Id}: {ex.Message}");
				}
			}

			TotalThings = (uint)_allThings.Count;
			RefreshAfterCatalogMutation(goToLastPage: true);

			EndThingTransaction(createdThings);
		}

		public void RemoveThing(ThingItemViewModel thing) => RemoveThings(new[] { thing });

		public void RemoveThings(IEnumerable<ThingItemViewModel> things)
		{
			if (_catalog == null) return;

			var itemsList = things.ToList();
			if (itemsList.Count == 0) return;

			var kind = SelectedSection;
			var affected = itemsList.Select(t => (kind, t.Id)).ToList();
			StartThingTransaction(affected);

			var idsToRemove = new HashSet<uint>(itemsList.Select(t => t.Id));

			if (SelectedThing != null && idsToRemove.Contains(SelectedThing.Id))
			{
				SelectedThing = null;
				NotifySelectionChanged();
			}

			// Sort descending to allow sequential deletion from the end
			var idsDescending = itemsList.Select(t => t.Id).Distinct().OrderByDescending(id => id).ToList();

			for (int i = 0; i < idsDescending.Count; i++)
			{
				var id = idsDescending[i];
				bool rebuild = (i == idsDescending.Count - 1);

				uint lastId = kind switch
				{
					ThingKind.Item => _catalog.ItemCount,
					ThingKind.Outfit => _catalog.OutfitCount,
					ThingKind.Effect => _catalog.EffectCount,
					ThingKind.Missile => _catalog.MissileCount,
					_ => 0
				};

				if (id == lastId)
				{
					switch (kind)
					{
						case ThingKind.Item:
							_catalog.RemoveItem(id, rebuild);
							break;
						case ThingKind.Outfit:
							_catalog.RemoveOutfit(id, rebuild);
							break;
						case ThingKind.Effect:
							_catalog.RemoveEffect(id, rebuild);
							break;
						case ThingKind.Missile:
							_catalog.RemoveMissile(id, rebuild);
							break;
					}
					_allThings.RemoveAll(t => t.Id == id);
				}
				else
				{
					var emptyThing = new ThingType { Id = id, Kind = kind };
					var fg = new ThingFrameGroup
					{
						GroupTypeId = 0,
						Width = 1,
						Height = 1,
						ExactSize = 32,
						Layers = 1,
						PatternX = 1,
						PatternY = 1,
						PatternZ = 1,
						Frames = 1,
						SpriteIds = new uint[1]
					};
					emptyThing.FrameGroups.Add(fg);

					switch (kind)
					{
						case ThingKind.Item:
							_catalog.PutItem(emptyThing, rebuild);
							break;
						case ThingKind.Outfit:
							_catalog.PutOutfit(emptyThing, rebuild);
							break;
						case ThingKind.Effect:
							_catalog.PutEffect(emptyThing, rebuild);
							break;
						case ThingKind.Missile:
							_catalog.PutMissile(emptyThing, rebuild);
							break;
					}

					var idx = _allThings.FindIndex(t => t.Id == id);
					if (idx >= 0)
					{
						_allThings[idx] = emptyThing;
					}
				}

				if (AddedThingIds.Contains(id))
				{
					AddedThingIds.Remove(id);
				}
				else
				{
					RemovedThingIds.Add(id);
				}
				ModifiedThingIds.Remove(id);
			}

			TotalThings = (uint)_allThings.Count;

			// Handle page overflow if the current page is now beyond the new total pages
			int maxPage = Math.Max(1, (int)((TotalThings + (uint)PageSize - 1) / (uint)PageSize));
			if (CurrentPage > maxPage)
			{
				_currentPage = maxPage;
				OnPropertyChanged(nameof(CurrentPage));
				OnPropertyChanged(nameof(HasNextPage));
				OnPropertyChanged(nameof(HasPreviousPage));
			}

			RefreshAfterCatalogMutation(goToLastPage: false);

			EndThingTransaction(affected);
		}

		[RelayCommand(CanExecute = nameof(IsArchiveLoaded))]
		private void ImportThing() => RequestImportNewThing();

		[RelayCommand(CanExecute = nameof(HasThingSelection))]
		private void ExportSelectedPng() => RequestExportThings(GetSelectedThings(), "png");

		[RelayCommand(CanExecute = nameof(HasThingSelection))]
		private void ExportSelectedJpeg() => RequestExportThings(GetSelectedThings(), "jpg");

		[RelayCommand(CanExecute = nameof(HasThingSelection))]
		private void ExportSelectedBmp() => RequestExportThings(GetSelectedThings(), "bmp");

		[RelayCommand(CanExecute = nameof(HasThingSelection))]
		private void ExportSelectedNyxThing() => RequestExportThings(GetSelectedThings(), "nyx-thing");

		[RelayCommand(CanExecute = nameof(HasThingSelection))]
		private void ExportSelectedObd() => RequestExportThings(GetSelectedThings(), "obd");

		[RelayCommand(CanExecute = nameof(HasThingSelection))]
		private void ExportSelectedThings() => RequestExportThings(GetSelectedThings());

		[RelayCommand(CanExecute = nameof(HasThingSelection))]
		private void DuplicateSelectedThings() => DuplicateThings(GetSelectedThings());

		[RelayCommand(CanExecute = nameof(HasThingSelection))]
		private void RemoveSelectedThings() => RemoveThings(GetSelectedThings());

		[RelayCommand(CanExecute = nameof(HasMultipleThingSelection))]
		private void EditSelectedThings() => OpenMultiThingEditor(GetSelectedThings());

		[RelayCommand(CanExecute = nameof(HasThingSelection))]
		private void ReplaceSelectedThings() => RequestReplaceThings(GetSelectedThings());

		[RelayCommand]
		private void NextPage()
		{
			if (HasNextPage)
				CurrentPage++;
		}

		[RelayCommand]
		private void PreviousPage()
		{
			if (HasPreviousPage)
				CurrentPage--;
		}

		[RelayCommand]
		private void FirstPage() => CurrentPage = 1;

		[RelayCommand]
		private void LastPage() => CurrentPage = TotalPages;

		[RelayCommand(CanExecute = nameof(IsArchiveLoaded))]
		private void NewThing()
		{
			if (_catalog == null) return;

			try
			{
				var kind = SelectedSection;
				var newId = ThingExchangeHelper.GetNextAppendId(_catalog, kind);

				StartThingTransaction(Enumerable.Empty<(ThingKind, uint)>());

				var newThing = new ThingType
				{
					Id = newId,
					Kind = kind
				};
				
				var fg = new ThingFrameGroup
				{
					GroupTypeId = 0,
					Width = 1,
					Height = 1,
					ExactSize = 32,
					Layers = 1,
					PatternX = 1,
					PatternY = 1,
					PatternZ = 1,
					Frames = 1,
					SpriteIds = new uint[1]
				};
				newThing.FrameGroups.Add(fg);

				switch (kind)
				{
					case ThingKind.Item:
						_catalog.PutItem(newThing);
						break;
					case ThingKind.Outfit:
						_catalog.PutOutfit(newThing);
						break;
					case ThingKind.Effect:
						_catalog.PutEffect(newThing);
						break;
					case ThingKind.Missile:
						_catalog.PutMissile(newThing);
						break;
				}
				
				_allThings.Add(newThing);
				AddedThingIds.Add(newId);
				_allThings.Sort((a, b) => a.Id.CompareTo(b.Id));
				TotalThings = (uint)_allThings.Count;
				
				HasSavedChanges = true;
				
				RefreshAfterCatalogMutation(goToLastPage: true);

				var newItem = PagedThings.LastOrDefault();
				if (newItem != null)
				{
					SelectThing(newItem);
					ScrollToItemRequested?.Invoke(newItem);
					_ = OpenThingEditor(newItem);
				}

				EndThingTransaction(new[] { (kind, newId) });
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[ThingsLoader] Failed to create new thing: {ex.Message}");
				_currentAction = null;
			}
		}

		private bool CanGoToId() =>
			IsArchiveLoaded
			&& TotalThings > 0
			&& uint.TryParse(_jumpToIdText.Trim(), out _);

		[RelayCommand(CanExecute = nameof(CanGoToId))]
		private async System.Threading.Tasks.Task GoToId()
		{
			if (!uint.TryParse(JumpToIdText.Trim(), out var enteredId))
				return;

			var internalId = ResolveInternalThingId(enteredId);
			var index = _allThings.FindIndex(t => t.Id == internalId);
			if (index < 0)
				return;

			CurrentPage = index / PageSize + 1;
			var thing = PagedThings.FirstOrDefault(t => t.Id == internalId);
			if (thing == null)
				return;

			if (await RequestSelectThing(thing))
			{
				ScrollToItemRequested?.Invoke(thing);
				await OpenThingEditor(thing);
			}
		}

		public event Action<object>? ScrollToItemRequested;

		private uint ResolveInternalThingId(uint enteredId)
		{
			if (SelectedSection != ThingKind.Item)
				return enteredId;

			var offset = SettingsViewModel.ThingIdOffset;
			var asDisplayed = enteredId >= offset ? enteredId - offset : enteredId;
			if (_allThings.Any(t => t.Id == asDisplayed))
				return asDisplayed;

			return enteredId;
		}

		private void StartThingTransaction(IEnumerable<(ThingKind Kind, uint Id)> affectedThings)
		{
			if (_catalog == null) return;

			_currentAction = new Services.Archive.ThingUndoAction
			{
				ItemCountBefore = _catalog.ItemCount,
				OutfitCountBefore = _catalog.OutfitCount,
				EffectCountBefore = _catalog.EffectCount,
				MissileCountBefore = _catalog.MissileCount,
				HasSavedChangesBefore = HasSavedChanges,
				AddedBefore = new HashSet<uint>(AddedThingIds),
				RemovedBefore = new HashSet<uint>(RemovedThingIds),
				ModifiedBefore = new HashSet<uint>(ModifiedThingIds)
			};

			foreach (var item in affectedThings)
			{
				var currentThing = GetThingFromCatalog(item.Kind, item.Id);
				if (currentThing != null)
				{
					_currentAction.ThingsBefore[item.Kind][item.Id] = ThingCloner.Clone(currentThing, item.Id);
				}
			}
		}

		private void EndThingTransaction(IEnumerable<(ThingKind Kind, uint Id)> affectedThings)
		{
			if (_catalog == null || _currentAction == null) return;

			_currentAction.ItemCountAfter = _catalog.ItemCount;
			_currentAction.OutfitCountAfter = _catalog.OutfitCount;
			_currentAction.EffectCountAfter = _catalog.EffectCount;
			_currentAction.MissileCountAfter = _catalog.MissileCount;
			_currentAction.HasSavedChangesAfter = HasSavedChanges;
			_currentAction.AddedAfter.UnionWith(AddedThingIds);
			_currentAction.RemovedAfter.UnionWith(RemovedThingIds);
			_currentAction.ModifiedAfter.UnionWith(ModifiedThingIds);

			foreach (var item in affectedThings)
			{
				var currentThing = GetThingFromCatalog(item.Kind, item.Id);
				if (currentThing != null)
				{
					_currentAction.ThingsAfter[item.Kind][item.Id] = ThingCloner.Clone(currentThing, item.Id);
				}
			}

			_undoRedoStack?.Push(_currentAction);
			_currentAction = null;
			RefreshUndoRedoCommands();
			CatalogChanged?.Invoke();
		}

		public Services.Archive.ThingUndoAction ApplyReplacementThings(
			IReadOnlyList<ThingType> things,
			bool addMissingTargetIds = false)
		{
			if (_catalog == null)
				throw new InvalidOperationException("A target Things archive must be loaded.");
			if (things.Count == 0)
				throw new InvalidOperationException("The replacement batch is empty.");

			var orderedThings = things.OrderBy(thing => thing.Kind).ThenBy(thing => thing.Id).ToList();
			var affected = orderedThings.Select(thing => (thing.Kind, thing.Id)).Distinct().ToList();
			if (affected.Count != orderedThings.Count)
				throw new InvalidOperationException("The replacement batch contains a duplicate Thing ID.");
			var nextIds = Enum.GetValues<ThingKind>().ToDictionary(kind => kind, kind => ThingExchangeHelper.GetNextAppendId(_catalog, kind));
			foreach (var thing in orderedThings)
			{
				if (GetThingFromCatalog(thing.Kind, thing.Id) != null)
					continue;
				if (!addMissingTargetIds)
					throw new InvalidOperationException("A target Thing no longer exists.");
				if (thing.Id != nextIds[thing.Kind])
					throw new InvalidOperationException($"Target {thing.Kind} #{thing.Id} cannot be appended because #{nextIds[thing.Kind]} must be added first.");
				nextIds[thing.Kind]++;
			}

			StartThingTransaction(affected);
			var action = _currentAction ?? throw new InvalidOperationException("Could not start the Thing replacement transaction.");
			try
			{
				foreach (var thing in orderedThings)
				{
					var isNew = GetThingFromCatalog(thing.Kind, thing.Id) == null;
					PutThingIntoCatalog(thing.Kind, ThingCloner.Clone(thing, thing.Id));
					if (isNew)
						AddedThingIds.Add(thing.Id);
					else if (!AddedThingIds.Contains(thing.Id))
						ModifiedThingIds.Add(thing.Id);
				}

				HasSavedChanges = true;
				InvalidateFlagUsageCountsCache();
				ReloadThingsForSection(preserveCurrentPage: true);
				EndThingTransaction(affected);
				return action;
			}
			catch
			{
				RestoreReplacementThings(action, discardUndo: true);
				throw;
			}
		}

		public void RollbackReplacementThings(Services.Archive.ThingUndoAction action) =>
			RestoreReplacementThings(action, discardUndo: true);

		private void RestoreReplacementThings(Services.Archive.ThingUndoAction action, bool discardUndo)
		{
			if (_catalog == null)
				return;
			if (discardUndo)
				_undoRedoStack?.DiscardLatestUndoIfMatches(action);
			RevertCounts(action.ItemCountBefore, action.OutfitCountBefore, action.EffectCountBefore, action.MissileCountBefore);
			foreach (var kind in new[] { ThingKind.Item, ThingKind.Outfit, ThingKind.Effect, ThingKind.Missile })
				foreach (var pair in action.ThingsBefore[kind])
					PutThingIntoCatalog(kind, pair.Value);
			AddedThingIds.Clear(); foreach (var id in action.AddedBefore) AddedThingIds.Add(id);
			RemovedThingIds.Clear(); foreach (var id in action.RemovedBefore) RemovedThingIds.Add(id);
			ModifiedThingIds.Clear(); foreach (var id in action.ModifiedBefore) ModifiedThingIds.Add(id);
			HasSavedChanges = action.HasSavedChangesBefore;
			_currentAction = null;
			InvalidateFlagUsageCountsCache();
			ReloadThingsForSection(preserveCurrentPage: true);
			RefreshUndoRedoCommands();
		}

		[RelayCommand(CanExecute = nameof(CanUndo))]
		private void Undo()
		{
			if (_undoRedoStack == null || _catalog == null)
				return;

			var action = _undoRedoStack.PopUndo();
			if (action != null)
			{
				int prevPage = CurrentPage;

				RevertCounts(action.ItemCountBefore, action.OutfitCountBefore, action.EffectCountBefore, action.MissileCountBefore);

				foreach (var kind in new[] { ThingKind.Item, ThingKind.Outfit, ThingKind.Effect, ThingKind.Missile })
				{
					foreach (var pair in action.ThingsBefore[kind])
					{
						PutThingIntoCatalog(kind, pair.Value);
					}
				}

				AddedThingIds.Clear();
				foreach (var id in action.AddedBefore) AddedThingIds.Add(id);

				RemovedThingIds.Clear();
				foreach (var id in action.RemovedBefore) RemovedThingIds.Add(id);

				ModifiedThingIds.Clear();
				foreach (var id in action.ModifiedBefore) ModifiedThingIds.Add(id);

				HasSavedChanges = action.HasSavedChangesBefore;

				ReloadThingsForSection();

				int maxPage = TotalPages;
				CurrentPage = Math.Clamp(prevPage, 1, maxPage);
			}
			RefreshUndoRedoCommands();
		}

		[RelayCommand(CanExecute = nameof(CanRedo))]
		private void Redo()
		{
			if (_undoRedoStack == null || _catalog == null)
				return;

			var action = _undoRedoStack.PopRedo();
			if (action != null)
			{
				int prevPage = CurrentPage;

				RevertCounts(action.ItemCountAfter, action.OutfitCountAfter, action.EffectCountAfter, action.MissileCountAfter);

				foreach (var kind in new[] { ThingKind.Item, ThingKind.Outfit, ThingKind.Effect, ThingKind.Missile })
				{
					foreach (var pair in action.ThingsAfter[kind])
					{
						PutThingIntoCatalog(kind, pair.Value);
					}
				}

				AddedThingIds.Clear();
				foreach (var id in action.AddedAfter) AddedThingIds.Add(id);

				RemovedThingIds.Clear();
				foreach (var id in action.RemovedAfter) RemovedThingIds.Add(id);

				ModifiedThingIds.Clear();
				foreach (var id in action.ModifiedAfter) ModifiedThingIds.Add(id);

				HasSavedChanges = action.HasSavedChangesAfter;

				ReloadThingsForSection();

				int maxPage = TotalPages;
				CurrentPage = Math.Clamp(prevPage, 1, maxPage);
			}
			RefreshUndoRedoCommands();
		}

		private bool CanUndo() => _undoRedoStack?.UndoCount > 0;
		private bool CanRedo() => _undoRedoStack?.RedoCount > 0;

		public bool CanUndoReplacement(Services.Archive.ThingUndoAction action) =>
			_undoRedoStack?.IsLatestUndo(action) == true;

		public bool CanRedoReplacement(Services.Archive.ThingUndoAction action) =>
			_undoRedoStack?.IsLatestRedo(action) == true;

		public bool TryUndoReplacement(Services.Archive.ThingUndoAction action)
		{
			if (!CanUndoReplacement(action)) return false;
			Undo();
			return true;
		}

		public bool TryRedoReplacement(Services.Archive.ThingUndoAction action)
		{
			if (!CanRedoReplacement(action)) return false;
			Redo();
			return true;
		}

		public void RefreshUndoRedoCommands()
		{
			UndoCommand.NotifyCanExecuteChanged();
			RedoCommand.NotifyCanExecuteChanged();
		}

		public void ClearUndoRedoStack()
		{
			_undoRedoStack?.Clear();
			RefreshUndoRedoCommands();
		}

		private ThingType? GetThingFromCatalog(ThingKind kind, uint id)
		{
			if (_catalog == null) return null;
			try
			{
				return ThingExchangeHelper.GetThingFromCatalog(_catalog, kind, id);
			}
			catch
			{
				return null;
			}
		}

		private void PutThingIntoCatalog(ThingKind kind, ThingType thing)
		{
			if (_catalog == null) return;
			switch (kind)
			{
				case ThingKind.Item: _catalog.PutItem(thing); break;
				case ThingKind.Outfit: _catalog.PutOutfit(thing); break;
				case ThingKind.Effect: _catalog.PutEffect(thing); break;
				case ThingKind.Missile: _catalog.PutMissile(thing); break;
			}
		}

		private void RevertCounts(uint items, uint outfits, uint effects, uint missiles)
		{
			if (_catalog == null) return;
			while (_catalog.ItemCount > items) _catalog.RemoveItem(_catalog.ItemCount, _catalog.ItemCount == items + 1);
			while (_catalog.OutfitCount > outfits) _catalog.RemoveOutfit(_catalog.OutfitCount, _catalog.OutfitCount == outfits + 1);
			while (_catalog.EffectCount > effects) _catalog.RemoveEffect(_catalog.EffectCount, _catalog.EffectCount == effects + 1);
			while (_catalog.MissileCount > missiles) _catalog.RemoveMissile(_catalog.MissileCount, _catalog.MissileCount == missiles + 1);
		}
	}
}
