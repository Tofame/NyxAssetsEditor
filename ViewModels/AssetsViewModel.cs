using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NyxAssetsEditor.Services;

namespace NyxAssetsEditor.ViewModels
{
	public partial class AssetsViewModel : ViewModelBase
	{
		private readonly SpriteRenderer _renderer = new SpriteRenderer();

		public ObservableCollection<PanelViewModelBase> ActivePanels { get; } = new ObservableCollection<PanelViewModelBase>();
		public ObservableCollection<PanelViewModelBase> FloatingPanels { get; } = new ObservableCollection<PanelViewModelBase>();
		public ObservableCollection<PanelViewModelBase> LeftDockedPanels { get; } = new ObservableCollection<PanelViewModelBase>();
		public ObservableCollection<PanelViewModelBase> CenterDockedPanels { get; } = new ObservableCollection<PanelViewModelBase>();
		public ObservableCollection<PanelViewModelBase> RightDockedPanels { get; } = new ObservableCollection<PanelViewModelBase>();

		public AssetsViewModel()
		{
			// Subscribe to defaults if settings change
			SettingsViewModel.DefaultPageSizeChanged += OnDefaultPageSizeChanged;
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
		}

		private void AddPanel(PanelViewModelBase panel)
		{
			panel.RequestClose += OnPanelRequestClose;
			panel.RequestDockStateChanged += OnPanelRequestDockStateChanged;
			panel.PropertyChanged += OnPanelPropertyChanged;

			ActivePanels.Add(panel);
			FloatingPanels.Add(panel);
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

		public bool IsLeftEmpty => LeftDockedPanels.Count == 0;
		public bool IsCenterEmpty => CenterDockedPanels.Count == 0;
		public bool IsRightEmpty => RightDockedPanels.Count == 0;

		private void UpdateColumnWidths()
		{
			OnPropertyChanged(nameof(IsLeftEmpty));
			OnPropertyChanged(nameof(IsCenterEmpty));
			OnPropertyChanged(nameof(IsRightEmpty));
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

			ActivePanels.Remove(panel);
			RemoveFromDockCollections(panel);
			UpdateColumnWidths();
			OnPropertyChanged(nameof(IsSpriteArchiveLoaded));
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
