using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NyxAssetsEditor.Services;

namespace NyxAssetsEditor.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
	private readonly SpriteRenderer _renderer = new SpriteRenderer();

	[ObservableProperty]
	private ViewModelBase _currentPage;

	public ObservableCollection<FloatingSpriteLoaderViewModel> ActivePanels { get; } = new ObservableCollection<FloatingSpriteLoaderViewModel>();

	public MainWindowViewModel()
	{
		_currentPage = new HomeViewModel();
		
		SettingsViewModel.DefaultPageSizeChanged += OnDefaultPageSizeChanged;
	}

	private void OnDefaultPageSizeChanged(int newPageSize)
	{
		foreach (var panel in ActivePanels)
		{
			panel.PageSize = newPageSize;
		}
	}

	[RelayCommand]
	private void SpawnSpritePanel()
	{
		SpawnSpritePanelWithFile(null);
	}

	public void SpawnSpritePanelWithFile(string? filePath)
	{
		var panel = new FloatingSpriteLoaderViewModel(_renderer)
		{
			PageSize = SettingsViewModel.DefaultPageSize,
			UseTransparentPixels = SettingsViewModel.UseTransparentPixels,
			UseExtendedSpriteIds = SettingsViewModel.UseExtendedSpriteIds,
			PositionX = 100 + ActivePanels.Count * 30,
			PositionY = 100 + ActivePanels.Count * 30,
			IsVisible = true
		};

		panel.RequestClose += (p) => ActivePanels.Remove(p);

		if (!string.IsNullOrEmpty(filePath))
		{
			panel.LoadArchive(filePath);
		}

		ActivePanels.Add(panel);
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
}