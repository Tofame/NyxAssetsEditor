using System.Collections.Generic;
using NyxAssetsEditor.ViewModels.Core;
using NyxAssetsEditor.ViewModels.Shell;

namespace NyxAssetsEditor.ViewModels.Pages
{
	public class HomeViewModel : ViewModelBase
	{
		private readonly MainWindowViewModel _mainWindow;

		public string Title => "Home Dashboard";
		public string Description => "Welcome to Nyx Assets Editor! Quick access to your recently opened archives.";

		public List<RecentCombinationItemViewModel> RecentCombinations { get; }

		// Parameterless constructor for design-time
		public HomeViewModel()
		{
			_mainWindow = null!;
			RecentCombinations = new List<RecentCombinationItemViewModel>();
		}

		public HomeViewModel(MainWindowViewModel mainWindow)
		{
			_mainWindow = mainWindow;

			var recents = NyxAssetsEditor.Services.Persistence.PersistenceService.GetRecentCombinations();
			RecentCombinations = new List<RecentCombinationItemViewModel>();
			foreach (var r in recents)
			{
				RecentCombinations.Add(new RecentCombinationItemViewModel(
					r.SpritePath,
					r.ThingsPath,
					this,
					r.SpriteGuessSettingsFromSignature,
					r.SpritePreferOtfiSettings,
					r.SpriteUseTransparentPixels,
					r.SpriteUseExtendedSpriteIds,
					r.ThingsGuessSettingsFromSignature,
					r.ThingsPreferOtfiSettings,
					r.ThingsUseExtendedThingIds,
					r.ThingsUseFrameAnimations,
					r.ThingsUseFrameGroups
				));
			}
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
			_mainWindow?.LoadCombination(
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
	}
}
