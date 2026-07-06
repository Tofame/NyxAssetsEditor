using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NyxAssetsEditor.ViewModels
{
	public class ThingItemViewModel : ViewModelBase
	{
		public uint Id { get; }
		public string Name { get; }
		public string Description { get; }

		public ThingItemViewModel(uint id)
		{
			Id = id;
			Name = $"Thing #{id}";
			Description = $"Auto-generated description for thing entity number {id}. Contains properties and attributes.";
		}
	}

	public partial class FloatingThingsLoaderViewModel : PanelViewModelBase
	{
		private string _filePath = "No things loaded";
		private uint _totalThings;
		private int _currentPage = 1;
		private int _pageSize = 100;
		private bool _useExtendedThingIds = true;

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

		public FloatingThingsLoaderViewModel()
		{
			// Default initialization
		}

		public void LoadArchive(string path)
		{
			FilePath = path;
			TotalThings = 320; // Mock 320 entities loaded from the dat/things archive
			CurrentPage = 1;
			UpdatePage();
		}

		private void UpdatePage()
		{
			PagedThings.Clear();
			if (TotalThings == 0) return;

			uint startId = (uint)((CurrentPage - 1) * PageSize + 1);
			uint endId = Math.Min((uint)(CurrentPage * PageSize), TotalThings);

			for (uint id = startId; id <= endId; id++)
			{
				PagedThings.Add(new ThingItemViewModel(id));
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
