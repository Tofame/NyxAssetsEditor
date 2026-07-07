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
		_currentPage = new HomeViewModel(this);
	}

	[RelayCommand]
	private void NavigateToHome()
	{
		CurrentPage = new HomeViewModel(this);
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

	public void LoadCombination(string spritePath, string thingsPath)
	{
		if (_assetsViewModel == null)
		{
			_assetsViewModel = new AssetsViewModel();
		}

		CurrentPage = _assetsViewModel;
		_assetsViewModel.LoadCombination(spritePath, thingsPath);
	}
}