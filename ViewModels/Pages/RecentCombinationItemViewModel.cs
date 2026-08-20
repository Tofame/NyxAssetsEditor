using CommunityToolkit.Mvvm.Input;
using System.IO;
using NyxAssetsEditor.ViewModels.Core;
using NyxAssetsEditor.ViewModels.Common;
using Avalonia.Input.Platform;

namespace NyxAssetsEditor.ViewModels.Pages
{
	public partial class RecentCombinationItemViewModel : ViewModelBase
	{
		private readonly HomeViewModel _parent;

		public string SpritePath { get; }
		public string ThingsPath { get; }

		// Sprite settings
		public bool SpriteGuessSettingsFromSignature { get; }
		public bool SpritePreferOtfiSettings { get; }
		public bool SpriteUseTransparentPixels { get; }
		public bool SpriteUseExtendedSpriteIds { get; }

		// Things settings
		public bool ThingsGuessSettingsFromSignature { get; }
		public bool ThingsPreferOtfiSettings { get; }
		public bool ThingsUseExtendedThingIds { get; }
		public bool ThingsUseFrameAnimations { get; }
		public bool ThingsUseFrameGroups { get; }

		public string DisplayName { get; }
		public string DetailsText { get; }
		public string ToolTipText { get; }
		public string ProjectName { get; private set; } = "";
		public bool HasProjectName => !string.IsNullOrEmpty(ProjectName);
		public bool HasBoth => !string.IsNullOrEmpty(SpritePath) && !string.IsNullOrEmpty(ThingsPath);
		public bool HasSpriteOnly => !string.IsNullOrEmpty(SpritePath) && string.IsNullOrEmpty(ThingsPath);
		public bool HasThingsOnly => string.IsNullOrEmpty(SpritePath) && !string.IsNullOrEmpty(ThingsPath);

		public bool IsOpen => _parent.IsCombinationOpen(SpritePath, ThingsPath);

		public bool ShowBoth => HasBoth && !IsOpen;
		public bool ShowSpriteOnly => HasSpriteOnly && !IsOpen;
		public bool ShowThingsOnly => HasThingsOnly && !IsOpen;
		public bool ShowDone => IsOpen && (HasBoth || HasThingsOnly);
		public bool ShowDoneSprites => IsOpen && HasSpriteOnly;

		public RecentCombinationItemViewModel(
			string spritePath,
			string thingsPath,
			HomeViewModel parent,
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
			SpritePath = spritePath;
			ThingsPath = thingsPath;
			_parent = parent;

			SpriteGuessSettingsFromSignature = spriteGuess;
			SpritePreferOtfiSettings = spritePreferOtfi;
			SpriteUseTransparentPixels = spriteTransparent;
			SpriteUseExtendedSpriteIds = spriteExtended;
			ThingsGuessSettingsFromSignature = thingsGuess;
			ThingsPreferOtfiSettings = thingsPreferOtfi;
			ThingsUseExtendedThingIds = thingsExtended;
			ThingsUseFrameAnimations = thingsAnimations;
			ThingsUseFrameGroups = thingsGroups;

			var presentation = ArchivePairPathPresentation.Create(spritePath, thingsPath);
			DisplayName = presentation.DisplayName;
			DetailsText = presentation.DetailsText;
			ToolTipText = presentation.ToolTipText;
			ProjectName = InferProjectName(presentation.DirectoryPath);
		}

		private string InferProjectName(string dirPath)
		{
			if (string.IsNullOrEmpty(dirPath))
				return "";

			char sep = Path.DirectorySeparatorChar;
			var parts = dirPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
			
			int projectIdx = -1;
			for (int i = 0; i < parts.Length; i++)
			{
				var part = parts[i].ToLowerInvariant();
				if (part == "data" || part == "things" || part == "assets" || part == "datprotocols" || 
				    (part.StartsWith("v") && part.Length > 1 && char.IsDigit(part[1])) ||
				    int.TryParse(part, out _))
				{
					if (i > 0)
					{
						var prev = parts[i - 1].ToLowerInvariant();
						if (prev != "desktop" && prev != "documents" && prev != "downloads" && prev != "users" && prev != "")
						{
							projectIdx = i - 1;
							break;
						}
					}
				}
			}

			if (projectIdx == -1)
			{
				if (parts.Length > 1)
				{
					var prev = parts[parts.Length - 2].ToLowerInvariant();
					if (prev != "desktop" && prev != "documents" && prev != "downloads" && prev != "users" && prev != "")
					{
						projectIdx = parts.Length - 2;
					}
					else
					{
						projectIdx = parts.Length - 1;
					}
				}
				else if (parts.Length == 1)
				{
					projectIdx = 0;
				}
			}

			if (projectIdx >= 0 && projectIdx < parts.Length)
			{
				var name = parts[projectIdx];
				if (name != "desktop" && name != "documents" && name != "downloads" && name != "users" && name != "")
				{
					return name;
				}
			}

			return "";
		}

		[RelayCommand]
		private async System.Threading.Tasks.Task Load()
		{
			var missing = new System.Collections.Generic.List<string>();
			if (!string.IsNullOrEmpty(SpritePath) && !File.Exists(SpritePath))
				missing.Add(SpritePath);
			if (!string.IsNullOrEmpty(ThingsPath) && !File.Exists(ThingsPath))
				missing.Add(ThingsPath);

			if (missing.Count > 0)
			{
				_parent.NotifyMissingRecentCombination(this, missing);
				return;
			}

			if (IsOpen)
			{
				if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
					desktop.MainWindow is { } mainWindow)
				{
					var dialog = new NyxAssetsEditor.Views.Shell.ConfirmOpenDialog();
					await dialog.ShowDialog(mainWindow);
					if (!dialog.Result)
					{
						return;
					}
				}
			}

			_parent.LoadCombination(
				SpritePath,
				ThingsPath,
				SpriteGuessSettingsFromSignature,
				SpritePreferOtfiSettings,
				SpriteUseTransparentPixels,
				SpriteUseExtendedSpriteIds,
				ThingsGuessSettingsFromSignature,
				ThingsPreferOtfiSettings,
				ThingsUseExtendedThingIds,
				ThingsUseFrameAnimations,
				ThingsUseFrameGroups
			);
		}

		[RelayCommand]
		private void Remove()
		{
			_parent.RemoveCombination(this);
		}

		[RelayCommand]
		private async System.Threading.Tasks.Task CopyPath()
		{
			var path = !string.IsNullOrEmpty(SpritePath) ? SpritePath : ThingsPath;
			if (string.IsNullOrEmpty(path)) return;

			if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
				desktop.MainWindow?.Clipboard is { } clipboard)
			{
				await clipboard.SetTextAsync(path);
			}
		}
	}
}
