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

			_previewImage = _panel.GetPreviewForThing(thing);
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
		private bool _useSuggestedSettings = true;
		private bool _preferOtfiSettings;
		private string _jumpToIdText = string.Empty;
		private Services.Archive.UndoRedoStack<Services.Archive.ThingUndoAction>? _undoRedoStack;
		private Services.Archive.ThingUndoAction? _currentAction;

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

		public uint GetDisplayedId(uint id) =>
			SelectedSection == ThingKind.Item ? id + SettingsViewModel.ThingIdOffset : id;

		[RelayCommand]
		private void SelectItemsSection() => SelectedSection = ThingKind.Item;

		[RelayCommand]
		private void SelectOutfitsSection() => SelectedSection = ThingKind.Outfit;

		[RelayCommand]
		private void SelectEffectsSection() => SelectedSection = ThingKind.Effect;

		[RelayCommand]
		private void SelectMissilesSection() => SelectedSection = ThingKind.Missile;

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

		public ThingCatalog? Catalog => _catalog;

		public ClientDataReadOptions GetWriteOptions() => new ClientDataReadOptions
		{
			ClientVersion = new ClientDataVersion { Value = SettingsViewModel.ClientVersion },
			ExtendedSpriteIds = UseExtendedThingIds,
			ImprovedAnimations = UseFrameAnimations,
			OutfitFrameGroups = UseFrameGroups,
			TransparentSprites = SettingsViewModel.UseTransparentPixels
		};

		public FloatingSpriteLoaderViewModel? LinkedSpritePanel { get; set; }

		public bool UseSuggestedSettings
		{
			get => _useSuggestedSettings;
			set
			{
				if (SetProperty(ref _useSuggestedSettings, value))
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
					if (value && UseSuggestedSettings) UseSuggestedSettings = false;
				}
			}
		}

		public bool CanEditManualSettings => !UseSuggestedSettings && !PreferOtfiSettings;

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
		}

		private void OnClientVersionChanged(uint newVersion)
		{
			if (UseSuggestedSettings && !PreferOtfiSettings)
				ResetSettingsToDefaults();
		}

		public void ResetSettingsToDefaults()
		{
			var version = new ClientDataVersion { Value = SettingsViewModel.ClientVersion };
			UseExtendedThingIds = DatThingFormatRules.UsesExtendedSpriteIdsByDefault(version);
			UseFrameAnimations = DatThingFormatRules.UsesImprovedAnimationsByDefault(version);
			UseFrameGroups = DatThingFormatRules.UsesOutfitFrameGroupsByDefault(version);
		}

		public void Dispose()
		{
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

		public void SyncThingInList(ThingType thing, bool replaceExisting)
		{
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

		public Avalonia.Media.Imaging.WriteableBitmap? GetPreviewForThing(ThingType thing)
		{
			var loader = GetActiveSpriteLoader();
			if (loader == null)
				return null;

			try
			{
				var pixels = ThingPreviewRenderer.RenderPreviewRgba(thing, loader);
				return pixels == null ? null : _renderer.Convert(pixels);
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

		private bool _hasSavedChanges;
		public bool HasSavedChanges
		{
			get => _hasSavedChanges;
			set
			{
				if (SetProperty(ref _hasSavedChanges, value))
				{
					_parentViewModel?.RefreshCompileCommands();
				}
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

		private void ReloadThingsForSection()
		{
			_allThings.Clear();
			foreach (var thing in EnumerateSelectedSection())
				_allThings.Add(thing);

			TotalThings = (uint)_allThings.Count;
			_selectionAnchor = null;
			SelectedThing = null;
			_currentPage = 1;
			OnPropertyChanged(nameof(CurrentPage));
			OnPropertyChanged(nameof(HasNextPage));
			OnPropertyChanged(nameof(HasPreviousPage));
			UpdatePage();
			NotifySelectionChanged();
		}

		public async Task CreateNewArchiveAsync(string format, uint clientVersion, bool useExtendedThingIds, bool useFrameAnimations, bool useFrameGroups)
		{
			AddedThingIds.Clear();
			RemovedThingIds.Clear();
			ModifiedThingIds.Clear();

			UseExtendedThingIds = useExtendedThingIds;
			UseFrameAnimations = useFrameAnimations;
			UseFrameGroups = useFrameGroups;

			FilePath = format.ToLower() == "dat" ? "Untitled.dat" : "Untitled.things";

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

			if (PreferOtfiSettings && path.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
			{
				var otfi = OtfiSettingsReader.ReadForArchive(path, out var warning);
				var missing = new List<string>();
				if (otfi != null && otfi.Extended == null) missing.Add("extended");
				if (otfi != null && otfi.FrameDurations == null) missing.Add("frame-durations");
				if (otfi != null && otfi.FrameGroups == null) missing.Add("frame-groups");
				if (otfi == null || missing.Count > 0)
				{
					PreferOtfiSettings = false;
					UseSuggestedSettings = true;
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

			if (path.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(path))
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
						ErrorMessage = $"Unsupported version\nSignature: 0x{signature:X8}";
						_catalog = null;
						OnPropertyChanged(nameof(IsArchiveLoaded));
						return;
					}
					else
					{
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
				OnPropertyChanged(nameof(IsArchiveLoaded));
				_allThings.Clear();
				for (uint i = 1; i <= 320; i++)
				{
					var mockThing = new ThingType { Id = i, Kind = ThingKind.Item };
					var mockGroup = new ThingFrameGroup { SpriteIds = new uint[] { i } };
					mockThing.FrameGroups.Add(mockGroup);
					_allThings.Add(mockThing);
				}
				TotalThings = (uint)_allThings.Count;
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
					NyxAssetsEditor.Services.Persistence.PersistenceService.AddRecentCombination(spritePath, thingsPath);
				}
			}
		}

		private static ThingCatalog ReadCatalogFromFile(string path, ClientDataReadOptions options)
		{
			byte[] bytes = System.IO.File.ReadAllBytes(path);
			if (path.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
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
			DuplicateSelectedThingsCommand.NotifyCanExecuteChanged();
			RemoveSelectedThingsCommand.NotifyCanExecuteChanged();
			EditSelectedThingsCommand.NotifyCanExecuteChanged();
			ReplaceSelectedThingsCommand.NotifyCanExecuteChanged();
		}

		public void RequestReplaceThing(ThingItemViewModel thing) =>
			RequestReplaceThings(new[] { thing });

		public void RequestReplaceThings(IEnumerable<ThingItemViewModel> things)
		{
			var list = things.ToList();
			if (list.Count == 0 || _catalog == null)
				return;

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

		public void RefreshUndoRedoCommands()
		{
			UndoCommand.NotifyCanExecuteChanged();
			RedoCommand.NotifyCanExecuteChanged();
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
