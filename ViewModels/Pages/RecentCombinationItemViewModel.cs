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
		public string ToolTipText { get; }
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
				string dir = Path.GetDirectoryName(thingsPath) ?? "";
				DetailsText = CompactPath(dir);
				ToolTipText = $"DAT: {thingsPath}\nSPR: {spritePath}";
			}
			else if (!string.IsNullOrEmpty(datName))
			{
				DisplayName = datName;
				string dir = Path.GetDirectoryName(thingsPath) ?? "";
				DetailsText = CompactPath(dir);
				ToolTipText = $"DAT: {thingsPath}";
			}
			else if (!string.IsNullOrEmpty(sprName))
			{
				DisplayName = sprName;
				string dir = Path.GetDirectoryName(spritePath) ?? "";
				DetailsText = CompactPath(dir);
				ToolTipText = $"SPR: {spritePath}";
			}
			else
			{
				DisplayName = "Unknown Archive";
				DetailsText = "";
				ToolTipText = "";
			}
		}

		private static string CompactPath(string path, int maxLength = 35)
		{
			if (string.IsNullOrEmpty(path))
				return "";

			if (path.Length <= maxLength)
				return path;

			var separator = Path.DirectorySeparatorChar;
			var parts = path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, System.StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length == 0)
				return path;

			string result = parts[parts.Length - 1];
			for (int i = parts.Length - 2; i >= 0; i--)
			{
				string candidate = parts[i] + separator + result;
				if (($"...{separator}{candidate}").Length > maxLength)
				{
					break;
				}
				result = candidate;
			}

			return $"...{separator}{result}";
		}

		[RelayCommand]
		private void Load()
		{
			_parent.LoadCombination(SpritePath, ThingsPath);
		}
	}
}
