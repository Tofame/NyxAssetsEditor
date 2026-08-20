using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NyxAssetsEditor.Services.Persistence;
using NyxAssetsEditor.Services.Rendering;
using NyxAssetsEditor.ViewModels.Core;
using NyxAssetsEditor.ViewModels.Pages;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;
using NyxAssetsEditor.ViewModels.Sprites;

namespace NyxAssetsEditor.ViewModels.Shell;

public partial class MainWindowViewModel : ViewModelBase
{
	private AssetsViewModel? _assetsViewModel;
	private PaintViewModel? _paintViewModel;

	[ObservableProperty]
	private ViewModelBase _currentPage;

	public MainWindowViewModel()
	{
		_assetsViewModel = new AssetsViewModel();
		
		_currentPage = SettingsViewModel.DefaultLaunchSection switch
		{
			SettingsViewModel.LaunchSection.Assets => _assetsViewModel,
			SettingsViewModel.LaunchSection.Paint => _paintViewModel = new PaintViewModel(this),
			SettingsViewModel.LaunchSection.Converter => new ConverterViewModel(),
			_ => new HomeViewModel(this)
		};
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
	private void NavigateToConverter()
	{
		CurrentPage = new ConverterViewModel();
	}

	[RelayCommand]
	private void NavigateToAssets()
	{
		CurrentPage = _assetsViewModel;
	}

	public void LoadCombination(
		string spritePath,
		string thingsPath,
		bool spriteGuess = true,
		bool spritePreferOtfi = false,
		bool spriteTransparent = true,
		bool spriteExtended = true,
		bool thingsGuess = true,
		bool thingsPreferOtfi = false,
		bool thingsExtended = true,
		bool thingsAnimations = true,
		bool thingsGroups = true)
	{
		CurrentPage = _assetsViewModel;
		_assetsViewModel.LoadCombination(
			spritePath,
			thingsPath,
			spriteGuess,
			spritePreferOtfi,
			spriteTransparent,
			spriteExtended,
			thingsGuess,
			thingsPreferOtfi,
			thingsExtended,
			thingsAnimations,
			thingsGroups
		);
	}

	[RelayCommand]
	private async System.Threading.Tasks.Task NavigateToPaint()
	{
		_paintViewModel ??= new PaintViewModel(this);
		CurrentPage = _paintViewModel;
		if (_paintViewModel.Sprite == null)
		{
			var state = PersistenceService.LoadPaintState();
			if (state != null)
				await _paintViewModel.TryRestoreStateAsync(state);
		}
	}

	public void EditSprite(SpriteViewModel sprite, FloatingSpriteLoaderViewModel panel)
	{
		_paintViewModel ??= new PaintViewModel(this);
		if (_paintViewModel.Sprite != sprite)
			_paintViewModel.InitializeWithSprite(sprite, panel);
		CurrentPage = _paintViewModel;
	}

	public bool IsCombinationOpen(string spritePath, string thingsPath)
	{
		if (_assetsViewModel == null) return false;

		bool isSpriteOpen = false;
		if (!string.IsNullOrEmpty(spritePath))
		{
			isSpriteOpen = _assetsViewModel.ActivePanels
				.OfType<FloatingSpriteLoaderViewModel>()
				.Any(p => string.Equals(p.FilePath, spritePath, System.StringComparison.OrdinalIgnoreCase));
		}

		bool isThingsOpen = false;
		if (!string.IsNullOrEmpty(thingsPath))
		{
			isThingsOpen = _assetsViewModel.ActivePanels
				.OfType<FloatingThingsLoaderViewModel>()
				.Any(p => string.Equals(p.FilePath, thingsPath, System.StringComparison.OrdinalIgnoreCase));
		}

		if (!string.IsNullOrEmpty(spritePath) && !string.IsNullOrEmpty(thingsPath))
		{
			return isSpriteOpen || isThingsOpen;
		}

		if (!string.IsNullOrEmpty(spritePath)) return isSpriteOpen;
		if (!string.IsNullOrEmpty(thingsPath)) return isThingsOpen;

		return false;
	}

	public bool IsHomeActive => CurrentPage is HomeViewModel;
	public bool IsSettingsActive => CurrentPage is SettingsViewModel;
	public bool IsAssetsActive => CurrentPage is AssetsViewModel;
	public bool IsPaintActive => CurrentPage is PaintViewModel;
	public bool IsConverterActive => CurrentPage is ConverterViewModel;

	partial void OnCurrentPageChanged(ViewModelBase? oldValue, ViewModelBase newValue)
	{
		OnPropertyChanged(nameof(IsHomeActive));
		OnPropertyChanged(nameof(IsSettingsActive));
		OnPropertyChanged(nameof(IsAssetsActive));
		OnPropertyChanged(nameof(IsPaintActive));
		OnPropertyChanged(nameof(IsConverterActive));
	}
}