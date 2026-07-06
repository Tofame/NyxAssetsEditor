using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace NyxAssetsEditor.ViewModels
{
	public partial class PanelViewModelBase : ViewModelBase
	{
		private bool _isVisible = true;
		private bool _isMinimized;
		private double _positionX = 100;
		private double _positionY = 100;
		private double _panelWidth = 430;
		private double _contentHeight = 500;
		private string _dockState = "Floating";

		public event Action<PanelViewModelBase>? RequestClose;
		public event Action<PanelViewModelBase>? RequestDockStateChanged;

		public bool IsVisible
		{
			get => _isVisible;
			set => SetProperty(ref _isVisible, value);
		}

		public bool IsMinimized
		{
			get => _isMinimized;
			set
			{
				if (SetProperty(ref _isMinimized, value))
				{
					OnPropertyChanged(nameof(ShowResizeHandles));
				}
			}
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

		public double PanelWidth
		{
			get => _panelWidth;
			set
			{
				if (SetProperty(ref _panelWidth, value))
				{
					OnPropertyChanged(nameof(DisplayWidth));
				}
			}
		}

		public double ContentHeight
		{
			get => _contentHeight;
			set
			{
				if (SetProperty(ref _contentHeight, value))
				{
					OnPropertyChanged(nameof(DisplayHeight));
				}
			}
		}

		public string DockState
		{
			get => _dockState;
			set
			{
				if (SetProperty(ref _dockState, value))
				{
					RequestDockStateChanged?.Invoke(this);
					OnPropertyChanged(nameof(IsFloating));
					OnPropertyChanged(nameof(ShowResizeHandles));
					OnPropertyChanged(nameof(DisplayWidth));
					OnPropertyChanged(nameof(DisplayHeight));
				}
			}
		}

		public bool IsFloating => DockState == "Floating";
		public bool ShowResizeHandles => IsFloating && !IsMinimized;
		public double DisplayWidth => IsFloating ? PanelWidth : double.NaN;
		public double DisplayHeight => IsFloating ? ContentHeight : double.NaN;

		[RelayCommand]
		public void ToggleMinimize()
		{
			IsMinimized = !IsMinimized;
		}

		[RelayCommand]
		public void ClosePanel()
		{
			IsVisible = false;
			RequestClose?.Invoke(this);
		}

		[RelayCommand]
		public void SetDockState(string state)
		{
			DockState = state;
		}

		[RelayCommand]
		public void Undock()
		{
			DockState = "Floating";
		}

		public bool IsDraggingVM { get; set; }
		public double DragClickX { get; set; }
		public double DragClickY { get; set; }
	}
}
