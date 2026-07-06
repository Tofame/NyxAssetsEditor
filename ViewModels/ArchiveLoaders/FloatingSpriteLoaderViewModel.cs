using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using NyxAssetsEditor.Services.Archive;
using NyxAssetsEditor.Services.ImportExport;
using NyxAssetsEditor.Services.Rendering;
using NyxAssetsEditor.ViewModels.Common;
using NyxAssetsEditor.ViewModels.Core;
using NyxAssetsEditor.ViewModels.Pages;
using NyxAssetsEditor.ViewModels.Sprites;

namespace NyxAssetsEditor.ViewModels.ArchiveLoaders
{
	public partial class FloatingSpriteLoaderViewModel : PanelViewModelBase, IDisposable
	{
		private readonly SpriteRenderer _renderer;
		private string _filePath = "No archive loaded";
		private uint _totalSprites;
		private int _currentPage = 1;
		private int _pageSize = 100;
		private bool _useTransparentPixels = true;
		private bool _useExtendedSpriteIds = true;
		private bool _showSaveConfirmation;
		private string _jumpToIdText = string.Empty;

		private bool _hasSavedChanges;
		public bool HasSavedChanges
		{
			get => _hasSavedChanges;
			set
			{
				if (SetProperty(ref _hasSavedChanges, value))
				{
					ParentViewModel?.RefreshCompileCommands();
				}
			}
		}

		public event EventHandler? RequestSaveAs;
		public event EventHandler<SpriteFileRequestEventArgs>? RequestSpriteFileDialog;

		public SpriteViewModel? SelectedSprite { get; private set; }
		private SpriteViewModel? _selectionAnchor;

		public SpriteLoader Loader { get; } = new SpriteLoader();
		public ObservableCollection<SpriteViewModel> PagedSprites { get; } = new ObservableCollection<SpriteViewModel>();

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

		public string FileName => string.IsNullOrEmpty(FilePath) || FilePath == "No archive loaded" ? "" : System.IO.Path.GetFileName(FilePath);

		public ArchiveFormat ArchiveFormat => ArchiveFormatHelper.FromPath(FilePath);

		public bool UseTransparentPixels
		{
			get => _useTransparentPixels;
			set
			{
				if (SetProperty(ref _useTransparentPixels, value))
				{
					SettingsViewModel.UseTransparentPixels = value;
				}
			}
		}

		public bool UseExtendedSpriteIds
		{
			get => _useExtendedSpriteIds;
			set
			{
				if (SetProperty(ref _useExtendedSpriteIds, value))
				{
					SettingsViewModel.UseExtendedSpriteIds = value;
				}
			}
		}


		public int[] AvailablePageSizes { get; } = { 50, 100, 200, 300 };

		public bool ShowSaveConfirmation
		{
			get => _showSaveConfirmation;
			set => SetProperty(ref _showSaveConfirmation, value);
		}

		public uint TotalSprites
		{
			get => _totalSprites;
			private set
			{
				if (SetProperty(ref _totalSprites, value))
				{
					OnPropertyChanged(nameof(TotalPages));
					OnPropertyChanged(nameof(HasNextPage));
					OnPropertyChanged(nameof(HasPreviousPage));
					OnPropertyChanged(nameof(IsArchiveLoaded));
					GoToIdCommand.NotifyCanExecuteChanged();
				}
			}
		}

		public bool IsArchiveLoaded => TotalSprites > 0;

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
		private void ToggleViewMode()
		{
			IsGridView = !IsGridView;
		}

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

		public int TotalPages => TotalSprites == 0 ? 0 : (int)((TotalSprites + PageSize - 1) / PageSize);

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

		private bool CanGoToId() =>
			TotalSprites > 0
			&& uint.TryParse(_jumpToIdText.Trim(), out var id)
			&& id >= 1
			&& id <= TotalSprites;

		public AssetsViewModel? ParentViewModel { get; set; }

		public int AssetDisplaySize => SettingsViewModel.AssetDisplaySize;
		public int ListBorderWidthHeight => AssetDisplaySize + 4;
		public int GridTileWidth => AssetDisplaySize + 40;
		public int GridTileHeight => AssetDisplaySize + 44;

		public FloatingSpriteLoaderViewModel(SpriteRenderer renderer)
		{
			_renderer = renderer;
			SettingsViewModel.AssetDisplaySizeChanged += OnAssetDisplaySizeChanged;
		}

		public void LoadArchive(string path) => _ = LoadArchiveAsync(path);

		public async Task LoadArchiveAsync(string path)
		{
			FilePath = path;
			await Task.Run(() =>
				Loader.OpenArchive(path, extendedSpriteIds: UseExtendedSpriteIds, transparentPixels: UseTransparentPixels))
				.ConfigureAwait(true);
			TotalSprites = Loader.SpriteCount;
			CurrentPage = 1;
			UpdatePage();
			ParentViewModel?.OnSpriteArchiveLoaded(this);
		}

		private void UpdatePage()
		{
			PagedSprites.Clear();
			if (TotalSprites == 0) return;

			uint startId = (uint)((CurrentPage - 1) * PageSize + 1);
			uint endId = Math.Min((uint)(CurrentPage * PageSize), TotalSprites);

			for (uint id = startId; id <= endId; id++)
			{
				PagedSprites.Add(new SpriteViewModel(id, this, Loader, _renderer));
			}
		}

		public void SelectSprite(SpriteViewModel sprite, bool shift = false, bool ctrl = false)
		{
			if (shift)
			{
				if (_selectionAnchor != null)
				{
					ClearSelection();
					var sprites = PagedSprites.OrderBy(s => s.Id).ToList();
					var anchorIdx = sprites.FindIndex(s => s.Id == _selectionAnchor.Id);
					var clickIdx = sprites.FindIndex(s => s.Id == sprite.Id);
					if (anchorIdx < 0)
						anchorIdx = clickIdx;
					if (clickIdx >= 0)
					{
						var start = Math.Min(anchorIdx, clickIdx);
						var end = Math.Max(anchorIdx, clickIdx);
						for (var i = start; i <= end; i++)
							SetSpriteSelected(sprites[i], true);
					}
				}
				else
				{
					ClearSelection();
					SetSpriteSelected(sprite, true);
					_selectionAnchor = sprite;
				}
			}
			else if (ctrl)
			{
				SetSpriteSelected(sprite, !sprite.IsSelected);
				_selectionAnchor = sprite;
			}
			else
			{
				ClearSelection();
				SetSpriteSelected(sprite, true);
				_selectionAnchor = sprite;
			}

			SelectedSprite = sprite;
			NotifySelectionChanged();
		}

		public IReadOnlyList<SpriteViewModel> GetSelectedSprites() =>
			PagedSprites.Where(s => s.IsSelected).OrderBy(s => s.Id).ToList();

		private void ClearSelection()
		{
			foreach (var sprite in PagedSprites)
				sprite.IsSelected = false;
		}

		private static void SetSpriteSelected(SpriteViewModel sprite, bool selected) =>
			sprite.IsSelected = selected;

		private void NotifySelectionChanged()
		{
			OnPropertyChanged(nameof(HasSpriteSelection));
			OnPropertyChanged(nameof(SelectedSpriteCount));
			OnPropertyChanged(nameof(CanPasteSelected));
			CopySelectedSpriteCommand.NotifyCanExecuteChanged();
			PasteSelectedSpriteCommand.NotifyCanExecuteChanged();
			RemoveSelectedSpritesCommand.NotifyCanExecuteChanged();
			ImportSelectedSpritesCommand.NotifyCanExecuteChanged();
			ExportSelectedPngCommand.NotifyCanExecuteChanged();
			ExportSelectedJpegCommand.NotifyCanExecuteChanged();
			ExportSelectedBmpCommand.NotifyCanExecuteChanged();
			PasteSelectedSpriteCommand.NotifyCanExecuteChanged();
		}

		public bool HasSpriteSelection => GetSelectedSprites().Count > 0;
		public int SelectedSpriteCount => GetSelectedSprites().Count;

		public void CopySprite(SpriteViewModel sprite) => CopySprites(new[] { sprite });

		public void CopySprites(IEnumerable<SpriteViewModel> sprites)
		{
			var list = sprites.ToList();
			if (list.Count == 0)
				return;

			SpriteClipboard.CopyMany(list.Select(s => s.GetPixels()));
			NotifyPasteAvailability();
		}

		public void PasteSprite(SpriteViewModel? sprite) => PasteSprites(sprite != null ? new[] { sprite } : GetSelectedSprites());

		public void PasteSprites(IEnumerable<SpriteViewModel> sprites)
		{
			var targets = sprites.OrderBy(s => s.Id).ToList();
			var clipboard = SpriteClipboard.GetAll();
			if (targets.Count == 0 || clipboard.Count == 0)
				return;

			for (var i = 0; i < targets.Count; i++)
			{
				var pixels = clipboard[Math.Min(i, clipboard.Count - 1)];
				Loader.SetSpritePixels(targets[i].Id, pixels);
				targets[i].InvalidatePreview();
			}
		}

		public void RequestImportSprites(IEnumerable<SpriteViewModel> sprites)
		{
			var list = sprites.ToList();
			if (list.Count == 0)
				return;

			RequestSpriteFileDialog?.Invoke(this, new SpriteFileRequestEventArgs(list, ""));
		}

		public void RequestExportSprites(IEnumerable<SpriteViewModel> sprites, string format)
		{
			var list = sprites.ToList();
			if (list.Count == 0)
				return;

			RequestSpriteFileDialog?.Invoke(this, new SpriteFileRequestEventArgs(list, format));
		}

		public void RequestReplaceSprite(SpriteViewModel sprite) =>
			RequestImportSprites(new[] { sprite });

		public void RequestExportSprite(SpriteViewModel sprite, string format) =>
			RequestExportSprites(new[] { sprite }, format);

		public void ReplaceSpritePixels(IEnumerable<SpriteViewModel> sprites, byte[] rgba)
		{
			foreach (var sprite in sprites)
			{
				Loader.SetSpritePixels(sprite.Id, rgba);
				sprite.InvalidatePreview();
			}
		}

		public void NotifyExternalArchiveMutation()
		{
			TotalSprites = Loader.SpriteCount;
			if (CurrentPage > TotalPages && TotalPages > 0)
				CurrentPage = TotalPages;
			else
				UpdatePage();
		}

		public void RemoveSprite(SpriteViewModel sprite) => RemoveSprites(new[] { sprite });

		public void RemoveSprites(IEnumerable<SpriteViewModel> sprites)
		{
			var ids = sprites.Select(s => s.Id).Distinct().OrderByDescending(id => id).ToList();
			if (ids.Count == 0)
				return;

			foreach (var id in ids)
			{
				if (id == Loader.SpriteCount)
					Loader.RemoveLastSprite();
				else
					Loader.ClearSprite(id);
			}

			TotalSprites = Loader.SpriteCount;
			_selectionAnchor = null;
			SelectedSprite = null;
			HasSavedChanges = true;
			if (CurrentPage > TotalPages && TotalPages > 0)
				CurrentPage = TotalPages;
			else
				UpdatePage();

			NotifySelectionChanged();
		}

		[RelayCommand(CanExecute = nameof(IsArchiveLoaded))]
		private void NewSprite()
		{
			Loader.AddNewSprite();
			TotalSprites = Loader.SpriteCount;
			HasSavedChanges = true;

			var lastPage = TotalPages;
			if (CurrentPage != lastPage)
				CurrentPage = lastPage;
			else
				UpdatePage();

			var newSprite = PagedSprites.LastOrDefault();
			if (newSprite != null)
				SelectSprite(newSprite);
		}

		public void NotifyPasteAvailability()
		{
			OnPropertyChanged(nameof(CanPasteSelected));
			PasteSelectedSpriteCommand.NotifyCanExecuteChanged();
			foreach (var item in PagedSprites)
				item.NotifyPasteAvailabilityChanged();
		}

		public bool CanPasteSelected => HasSpriteSelection && SpriteClipboard.HasData;

		[RelayCommand(CanExecute = nameof(HasSpriteSelection))]
		private void CopySelectedSprite() => CopySprites(GetSelectedSprites());

		[RelayCommand(CanExecute = nameof(CanPasteSelected))]
		private void PasteSelectedSprite() => PasteSprites(GetSelectedSprites());

		[RelayCommand(CanExecute = nameof(HasSpriteSelection))]
		private void RemoveSelectedSprites() => RemoveSprites(GetSelectedSprites());

		[RelayCommand(CanExecute = nameof(HasSpriteSelection))]
		private void ImportSelectedSprites() => RequestImportSprites(GetSelectedSprites());

		[RelayCommand(CanExecute = nameof(HasSpriteSelection))]
		private void ExportSelectedPng() => RequestExportSprites(GetSelectedSprites(), "png");

		[RelayCommand(CanExecute = nameof(HasSpriteSelection))]
		private void ExportSelectedJpeg() => RequestExportSprites(GetSelectedSprites(), "jpg");

		[RelayCommand(CanExecute = nameof(HasSpriteSelection))]
		private void ExportSelectedBmp() => RequestExportSprites(GetSelectedSprites(), "bmp");

		public void HandleCopyShortcut()
		{
			if (HasSpriteSelection)
				CopySprites(GetSelectedSprites());
			else if (SelectedSprite != null)
				CopySprite(SelectedSprite);
		}

		public void HandlePasteShortcut()
		{
			if (HasSpriteSelection)
				PasteSprites(GetSelectedSprites());
			else if (SelectedSprite != null)
				PasteSprite(SelectedSprite);
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

		[RelayCommand(CanExecute = nameof(CanGoToId))]
		private void GoToId()
		{
			if (!uint.TryParse(JumpToIdText.Trim(), out var id) || id < 1 || id > TotalSprites)
				return;

			CurrentPage = (int)((id - 1) / PageSize + 1);
			var sprite = PagedSprites.FirstOrDefault(s => s.Id == id);
			if (sprite == null)
				return;

			SelectSprite(sprite);
			ScrollToItemRequested?.Invoke(sprite);
		}

		public event Action<object>? ScrollToItemRequested;


		[RelayCommand]
		private void RequestSaveConfirmation()
		{
			if (string.IsNullOrEmpty(FilePath) || FilePath == "No archive loaded") return;
			ShowSaveConfirmation = true;
		}

		[RelayCommand]
		private void CancelSave()
		{
			ShowSaveConfirmation = false;
		}

		[RelayCommand]
		private void ConfirmSave()
		{
			ShowSaveConfirmation = false;
			if (string.IsNullOrEmpty(FilePath) || !System.IO.File.Exists(FilePath)) return;

			try
			{
				string backupPath = FilePath + ".bak";
				System.IO.File.Copy(FilePath, backupPath, true);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Failed to save backup: {ex.Message}");
			}
		}

		[RelayCommand]
		private void SaveAs()
		{
			if (string.IsNullOrEmpty(FilePath) || FilePath == "No archive loaded") return;
			RequestSaveAs?.Invoke(this, EventArgs.Empty);
		}

		public void Dispose()
		{
			SettingsViewModel.AssetDisplaySizeChanged -= OnAssetDisplaySizeChanged;
			Loader.Dispose();
		}

		private void OnAssetDisplaySizeChanged(int newSize)
		{
			OnPropertyChanged(nameof(AssetDisplaySize));
			OnPropertyChanged(nameof(ListBorderWidthHeight));
			OnPropertyChanged(nameof(GridTileWidth));
			OnPropertyChanged(nameof(GridTileHeight));
		}
	}
}