using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NyxAssetsEditor.Services;

namespace NyxAssetsEditor.ViewModels
{
	public partial class FloatingSpriteLoaderViewModel : ViewModelBase, IDisposable
	{
		private readonly SpriteRenderer _renderer;
		private string _filePath = "No archive loaded";
		private uint _totalSprites;
		private int _currentPage = 1;
		private int _pageSize = 100;
		private bool _isVisible;
		private bool _isMinimized;
		private double _positionX = 100;
		private double _positionY = 100;
		private bool _useTransparentPixels = true;
		private bool _useExtendedSpriteIds = true;
		private double _panelWidth = 380;
		private double _contentHeight = 400;

		public event Action<FloatingSpriteLoaderViewModel>? RequestClose;

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

		public bool UseTransparentPixels
		{
			get => _useTransparentPixels;
			set => SetProperty(ref _useTransparentPixels, value);
		}

		public bool UseExtendedSpriteIds
		{
			get => _useExtendedSpriteIds;
			set => SetProperty(ref _useExtendedSpriteIds, value);
		}

		public double PanelWidth
		{
			get => _panelWidth;
			set => SetProperty(ref _panelWidth, value);
		}

		public double ContentHeight
		{
			get => _contentHeight;
			set => SetProperty(ref _contentHeight, value);
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
				}
			}
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

		public bool IsVisible
		{
			get => _isVisible;
			set => SetProperty(ref _isVisible, value);
		}

		public bool IsMinimized
		{
			get => _isMinimized;
			set => SetProperty(ref _isMinimized, value);
		}

		public double PositionX
		{
			get => _positionX;
			set => SetProperty(ref _positionX, value);
		}

		public double PositionY
		{
			get => _positionY;
			set => SetProperty(ref _positionY, value);
		}

		public FloatingSpriteLoaderViewModel(SpriteRenderer renderer)
		{
			_renderer = renderer;
		}

		public void LoadArchive(string path)
		{
			FilePath = path;
			Loader.OpenArchive(path, extendedSpriteIds: UseExtendedSpriteIds, transparentPixels: UseTransparentPixels);
			TotalSprites = Loader.SpriteCount;
			CurrentPage = 1;
			UpdatePage();
		}

		private void UpdatePage()
		{
			PagedSprites.Clear();
			if (TotalSprites == 0) return;

			uint startId = (uint)((CurrentPage - 1) * PageSize + 1);
			uint endId = Math.Min((uint)(CurrentPage * PageSize), TotalSprites);

			for (uint id = startId; id <= endId; id++)
			{
				PagedSprites.Add(new SpriteViewModel(id, Loader, _renderer));
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

		[RelayCommand]
		private void ToggleMinimize()
		{
			IsMinimized = !IsMinimized;
		}

		[RelayCommand]
		private void ClosePanel()
		{
			IsVisible = false;
			RequestClose?.Invoke(this);
		}

		public void Dispose()
		{
			Loader.Dispose();
		}
	}
}