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
				RecentCombinations.Add(new RecentCombinationItemViewModel(r.SpritePath, r.ThingsPath, this));
			}
		}

		public void LoadCombination(string spritePath, string thingsPath)
		{
			_mainWindow?.LoadCombination(spritePath, thingsPath);
		}
	}
}
