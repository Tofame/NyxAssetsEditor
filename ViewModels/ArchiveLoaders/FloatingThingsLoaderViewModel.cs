using System;
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
		private void Edit() => WithSelection(_panel.RequestEditThings, _panel.RequestEditThing);

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
		private string _jumpToIdText = string.Empty;

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

		public int[] AvailablePageSizes { get; } = { 25, 50, 100, 200 };

		public bool HasThingSelection => GetSelectedThings().Count > 0;
		public int SelectedThingCount => GetSelectedThings().Count;

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
		}

		private void OnThingIdOffsetChanged(uint newOffset)
		{
			foreach (var item in PagedThings)
				item.NotifyDisplayedIdChanged();
		}

		private void OnClientVersionChanged(uint newVersion) => ResetSettingsToDefaults();

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

		public void NotifySpriteLinkChanged()
		{
			OnPropertyChanged(nameof(IsSpriteLoaderLoaded));
			RefreshPreviews();
		}

		public void RefreshPreviews()
		{
			OnPropertyChanged(nameof(IsSpriteLoaderLoaded));
			foreach (var item in PagedThings)
				item.InvalidatePreview();
		}

		public void ApplyThingEdit(ThingType thing)
		{
			if (_catalog == null)
				return;

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

		public void LoadArchive(string path, bool useLastLoadedSprite = true) =>
			_ = LoadArchiveAsync(path, useLastLoadedSprite);

		public async Task LoadArchiveAsync(string path, bool useLastLoadedSprite = true)
		{
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

		public void RequestEditThing(ThingItemViewModel thing) =>
			RequestEditThings(new[] { thing });

		public void RequestEditThings(IEnumerable<ThingItemViewModel> things)
		{
			var list = things.ToList();
			if (list.Count == 0)
				return;

			RequestThingFileDialog?.Invoke(this, new ThingFileRequestEventArgs(list, "nyx-thing"));
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
				return;
			}

			RefreshAfterCatalogMutation(goToLastPage: !replaceExisting);
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

			foreach (var item in things.OrderBy(t => t.Id))
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
		}

		public void RemoveThing(ThingItemViewModel thing) => RemoveThings(new[] { thing });

		public void RemoveThings(IEnumerable<ThingItemViewModel> things)
		{
			if (_catalog == null) return;

			var itemsList = things.ToList();
			if (itemsList.Count == 0) return;

			var idsToRemove = new HashSet<uint>(itemsList.Select(t => t.Id));

			if (SelectedThing != null && idsToRemove.Contains(SelectedThing.Id))
			{
				SelectedThing = null;
				NotifySelectionChanged();
			}

			// Sort descending to allow sequential deletion from the end
			var idsDescending = itemsList.Select(t => t.Id).Distinct().OrderByDescending(id => id).ToList();
			var kind = SelectedSection;

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

		[RelayCommand(CanExecute = nameof(HasThingSelection))]
		private void EditSelectedThings() => RequestEditThings(GetSelectedThings());

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
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[ThingsLoader] Failed to create new thing: {ex.Message}");
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
	}
}
