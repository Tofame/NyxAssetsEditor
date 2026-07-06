using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NyxAssetsEditor.Services;
using NyxAssets.Things;
using NyxAssets.Things.Exchange;

namespace NyxAssetsEditor.ViewModels
{
	public partial class ThingItemViewModel : ViewModelBase
	{
		private readonly FloatingThingsLoaderViewModel _panel;
		private Avalonia.Media.Imaging.WriteableBitmap? _previewImage;
		private bool _isSelected;

		public uint Id { get; }

		public uint DisplayedId => Id + SettingsViewModel.ThingIdOffset;

		public bool IsSelected
		{
			get => _isSelected;
			set => SetProperty(ref _isSelected, value);
		}

		public Avalonia.Media.Imaging.WriteableBitmap? PreviewImage
		{
			get => _previewImage;
			set => SetProperty(ref _previewImage, value);
		}

		public ThingItemViewModel(uint id, Avalonia.Media.Imaging.WriteableBitmap? previewImage, FloatingThingsLoaderViewModel panel)
		{
			Id = id;
			_previewImage = previewImage;
			_panel = panel;
		}

		public void NotifyDisplayedIdChanged() => OnPropertyChanged(nameof(DisplayedId));

		[RelayCommand]
		private void Replace() => WithSelection(_panel.RequestReplaceThings, _panel.RequestReplaceThing);

		[RelayCommand]
		private void Edit() => WithSelection(_panel.RequestEditThings, _panel.RequestEditThing);

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
		private const string PendingRemoveMessage =
			"Remove is not supported by NyxAssets yet — things cannot be deleted from a catalog.";

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

		private ThingItemViewModel? _selectionAnchor;

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
					OnPropertyChanged(nameof(IsArchiveLoaded));
					ImportThingCommand.NotifyCanExecuteChanged();
				}
			}
		}

		public bool IsArchiveLoaded => TotalThings > 0;

		private bool _isGridView = true;

		public bool IsGridView
		{
			get => _isGridView;
			set
			{
				if (SetProperty(ref _isGridView, value))
				{
					OnPropertyChanged(nameof(IsListView));
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

		public int[] AvailablePageSizes { get; } = { 25, 50, 100, 200 };

		public bool HasThingSelection => GetSelectedThings().Count > 0;
		public int SelectedThingCount => GetSelectedThings().Count;

		public FloatingThingsLoaderViewModel(AssetsViewModel? parentViewModel = null)
		{
			_parentViewModel = parentViewModel;
			SettingsViewModel.ThingIdOffsetChanged += OnThingIdOffsetChanged;
			SettingsViewModel.ClientVersionChanged += OnClientVersionChanged;
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
		}

		public SpriteLoader? GetActiveSpriteLoader()
		{
			var spritePanel = _parentViewModel?.ResolveSpritePanelFor(this);
			return spritePanel is { IsArchiveLoaded: true } ? spritePanel.Loader : null;
		}

		public ThingType? GetThingType(uint id) => _allThings.Find(t => t.Id == id);

		public void SyncThingInList(ThingType thing, bool replaceExisting)
		{
			var idx = _allThings.FindIndex(t => t.Id == thing.Id);
			if (idx >= 0)
			{
				if (replaceExisting)
					_allThings[idx] = thing;
			}
			else if (thing.Kind == ThingKind.Item)
			{
				_allThings.Add(thing);
				_allThings.Sort((a, b) => a.Id.CompareTo(b.Id));
			}

			TotalThings = (uint)_allThings.Count;
		}

		public void RefreshAfterCatalogMutation(bool goToLastPage = false)
		{
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
			{
				Console.WriteLine($"[ThingsLoader] Preview failed for ThingID {thing.Id}: SpriteLoader is null or not loaded.");
				return null;
			}

			if (thing.FrameGroups == null || thing.FrameGroups.Count == 0)
			{
				Console.WriteLine($"[ThingsLoader] Preview failed for ThingID {thing.Id}: FrameGroups is null or empty.");
				return null;
			}

			var group = thing.FrameGroups[0];
			if (group.SpriteIds == null || group.SpriteIds.Length == 0)
			{
				Console.WriteLine($"[ThingsLoader] Preview failed for ThingID {thing.Id}: SpriteIds is null or empty.");
				return null;
			}

			uint spriteId = group.SpriteIds[0];
			if (spriteId == 0)
				return null;

			try
			{
				var pixels = loader.LoadSpritePixels(spriteId);
				return _renderer.Convert(pixels);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[ThingsLoader] Preview failed for ThingID {thing.Id} (SpriteID {spriteId}): {ex.Message}");
				return null;
			}
		}

		public bool IsSpriteLoaderLoaded => GetActiveSpriteLoader() != null;

		public void RefreshPreviews()
		{
			OnPropertyChanged(nameof(IsSpriteLoaderLoaded));
			foreach (var item in PagedThings)
			{
				var thing = GetThingType(item.Id);
				if (thing != null)
					item.PreviewImage = GetPreviewForThing(thing);
			}
		}

		public void LoadArchive(string path, bool useLastLoadedSprite = true)
		{
			var thingsFormat = ArchiveFormatHelper.FromPath(path);
			if (useLastLoadedSprite)
				_parentViewModel?.LinkThingsToSprite(this, thingsFormat);

			if (_parentViewModel?.ResolveSpritePanelFor(this) is not { IsArchiveLoaded: true })
				return;

			FilePath = path;
			try
			{
				byte[] bytes = System.IO.File.ReadAllBytes(path);
				var options = GetWriteOptions();

				if (path.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
				{
					var reader = new DatThingCatalogReader();
					_catalog = reader.Read(bytes, options);
				}
				else
				{
					var reader = new JsonThingCatalogReader();
					_catalog = reader.Read(bytes, options);
				}

				_allThings.Clear();
				if (_catalog != null)
				{
					foreach (var item in _catalog.EnumerateItems())
						_allThings.Add(item);
				}

				TotalThings = (uint)_allThings.Count;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[ThingsLoader] FAILED TO LOAD DAT/THINGS: {ex}");
				Debug.WriteLine($"Failed to load catalog: {ex.Message}");
				_allThings.Clear();
				for (uint i = 1; i <= 320; i++)
				{
					var mockThing = new ThingType { Id = i };
					var mockGroup = new ThingFrameGroup { SpriteIds = new uint[] { i } };
					mockThing.FrameGroups.Add(mockGroup);
					_allThings.Add(mockThing);
				}
				TotalThings = (uint)_allThings.Count;
			}

			_selectionAnchor = null;
			SelectedThing = null;
			CurrentPage = 1;
			UpdatePage();
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
				var preview = GetPreviewForThing(thing);
				PagedThings.Add(new ThingItemViewModel(thing.Id, preview, this));
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
					SyncThingInList(thing, replaceExisting);
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
			var ids = string.Join(", ", things.Select(t => t.DisplayedId));
			Debug.WriteLine($"[ThingsLoader] Remove thing(s) {ids}: {PendingRemoveMessage}");
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
	}
}
