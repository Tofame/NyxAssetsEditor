using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NyxAssetsEditor.Services.Rendering;
using NyxAssetsEditor.ViewModels.Core;
using NyxAssetsEditor.ViewModels.Pages;

namespace NyxAssetsEditor.ViewModels.Shell;

public partial class MainWindowViewModel : ViewModelBase
{
	private AssetsViewModel? _assetsViewModel;

	[ObservableProperty]
	private ViewModelBase _currentPage;

	public MainWindowViewModel()
	{
		_currentPage = new HomeViewModel();
	}

	[RelayCommand]
	private void NavigateToHome()
	{
		CurrentPage = new HomeViewModel();
	}

	[RelayCommand]
	private void NavigateToSettings()
	{
		CurrentPage = new SettingsViewModel();
	}

	[RelayCommand]
	private void NavigateToAssets()
	{
		CurrentPage = _assetsViewModel ??= new AssetsViewModel();
	}
}