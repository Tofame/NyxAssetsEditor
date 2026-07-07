using CommunityToolkit.Mvvm.Input;
using System.IO;
using NyxAssetsEditor.ViewModels.Core;

namespace NyxAssetsEditor.ViewModels.Pages
{
	public partial class RecentCombinationItemViewModel : ViewModelBase
	{
		private readonly HomeViewModel _parent;

		public string SpritePath { get; }
		public string ThingsPath { get; }

		public string DisplayName { get; }
		public string DetailsText { get; }
		public bool HasBoth => !string.IsNullOrEmpty(SpritePath) && !string.IsNullOrEmpty(ThingsPath);
		public bool HasSpriteOnly => !string.IsNullOrEmpty(SpritePath) && string.IsNullOrEmpty(ThingsPath);
		public bool HasThingsOnly => string.IsNullOrEmpty(SpritePath) && !string.IsNullOrEmpty(ThingsPath);

		public RecentCombinationItemViewModel(string spritePath, string thingsPath, HomeViewModel parent)
		{
			SpritePath = spritePath;
			ThingsPath = thingsPath;
			_parent = parent;

			string sprName = string.IsNullOrEmpty(spritePath) ? "" : Path.GetFileName(spritePath);
			string datName = string.IsNullOrEmpty(thingsPath) ? "" : Path.GetFileName(thingsPath);

			if (!string.IsNullOrEmpty(sprName) && !string.IsNullOrEmpty(datName))
			{
				DisplayName = $"{datName} + {sprName}";
				DetailsText = Path.GetDirectoryName(thingsPath) ?? "";
			}
			else if (!string.IsNullOrEmpty(datName))
			{
				DisplayName = datName;
				DetailsText = Path.GetDirectoryName(thingsPath) ?? "";
			}
			else if (!string.IsNullOrEmpty(sprName))
			{
				DisplayName = sprName;
				DetailsText = Path.GetDirectoryName(spritePath) ?? "";
			}
			else
			{
				DisplayName = "Unknown Archive";
				DetailsText = "";
			}
		}

		[RelayCommand]
		private void Load()
		{
			_parent.LoadCombination(SpritePath, ThingsPath);
		}
	}
}
