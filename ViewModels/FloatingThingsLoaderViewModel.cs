using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NyxAssetsEditor.Services;
using NyxAssets.Things;

namespace NyxAssetsEditor.ViewModels
{
	public class ThingItemViewModel : ViewModelBase
	{
		private Avalonia.Media.Imaging.WriteableBitmap? _previewImage;

		public uint Id { get; }

		public uint DisplayedId => Id + SettingsViewModel.ThingIdOffset;

		public Avalonia.Media.Imaging.WriteableBitmap? PreviewImage
		{
			get => _previewImage;
			set => SetProperty(ref _previewImage, value);
		}

		public ThingItemViewModel(uint id, Avalonia.Media.Imaging.WriteableBitmap? previewImage)
		{
			Id = id;
			_previewImage = previewImage;
		}

		public void NotifyDisplayedIdChanged()
		{
			OnPropertyChanged(nameof(DisplayedId));
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
				}
			}
		}

		public bool IsArchiveLoaded => TotalThings > 0;

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
			{
				item.NotifyDisplayedIdChanged();
			}
		}

		private void OnClientVersionChanged(uint newVersion)
		{
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
		}

		public SpriteLoader? GetActiveSpriteLoader()
		{
			if (_parentViewModel != null)
			{
				Console.WriteLine($"[ThingsLoader] GetActiveSpriteLoader: ActivePanels Count = {_parentViewModel.ActivePanels.Count}");
				foreach (var panel in _parentViewModel.ActivePanels)
				{
					if (panel is FloatingSpriteLoaderViewModel spritePanel)
					{
						Console.WriteLine($"[ThingsLoader] Found SpritePanel. IsArchiveLoaded = {spritePanel.IsArchiveLoaded}, SpriteCount = {spritePanel.Loader.SpriteCount}");
						if (spritePanel.IsArchiveLoaded)
						{
							return spritePanel.Loader;
						}
					}
					else
					{
						Console.WriteLine($"[ThingsLoader] Found other panel: {panel.GetType().Name}");
					}
				}
			}
			else
			{
				Console.WriteLine("[ThingsLoader] parentViewModel is null!");
			}
			return null;
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
			{
				return null;
			}

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
				var thing = _allThings.Find(t => t.Id == item.Id);
				if (thing != null)
				{
					item.PreviewImage = GetPreviewForThing(thing);
				}
			}
		}

		public void LoadArchive(string path)
		{
			if (!IsSpriteLoaderLoaded) return;
			FilePath = path;
			try
			{
				byte[] bytes = System.IO.File.ReadAllBytes(path);
				var options = new ClientDataReadOptions
				{
					ClientVersion = new ClientDataVersion { Value = SettingsViewModel.ClientVersion },
					ExtendedSpriteIds = UseExtendedThingIds,
					ImprovedAnimations = UseFrameAnimations,
					OutfitFrameGroups = UseFrameGroups,
					TransparentSprites = SettingsViewModel.UseTransparentPixels
				};
				
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
					{
						_allThings.Add(item);
					}
				}

				TotalThings = (uint)_allThings.Count;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[ThingsLoader] FAILED TO LOAD DAT/THINGS: {ex}");
				System.Diagnostics.Debug.WriteLine($"Failed to load catalog: {ex.Message}");
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
				PagedThings.Add(new ThingItemViewModel(thing.Id, preview));
			}
		}

		[RelayCommand]
		private void NextPage()
		{
			if (HasNextPage)
			{
				CurrentPage++;
			}
		}

		[RelayCommand]
		private void PreviousPage()
		{
			if (HasPreviousPage)
			{
				CurrentPage--;
			}
		}

		[RelayCommand]
		private void FirstPage()
		{
			CurrentPage = 1;
		}

		[RelayCommand]
		private void LastPage()
		{
			CurrentPage = TotalPages;
		}
	}
}
