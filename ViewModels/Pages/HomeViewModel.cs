using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using NyxAssetsEditor.ViewModels.Core;
using NyxAssetsEditor.ViewModels.Shell;

namespace NyxAssetsEditor.ViewModels.Pages
{
	public class HomeViewModel : ViewModelBase
	{
		private readonly MainWindowViewModel _mainWindow;

		public string Title => "Home Dashboard";
		public string Description => "Welcome to Nyx Assets Editor! Quick access to your recently opened archives.";

		public ObservableCollection<RecentCombinationItemViewModel> RecentCombinations { get; }

		private bool _showSpr;
		public bool ShowSpr
		{
			get => _showSpr;
			set
			{
				if (SetProperty(ref _showSpr, value))
				{
					OnPropertyChanged(nameof(FilteredRecentCombinations));
					OnPropertyChanged(nameof(HasFilteredCombinations));
				}
			}
		}

		public System.Collections.Generic.List<RecentCombinationItemViewModel> FilteredRecentCombinations
		{
			get
			{
				var list = new System.Collections.Generic.List<RecentCombinationItemViewModel>();
				foreach (var item in RecentCombinations)
				{
					if (_showSpr || item.HasBoth)
					{
						list.Add(item);
					}
				}
				return list;
			}
		}

		public bool HasFilteredCombinations => FilteredRecentCombinations.Count > 0;

		private string? _statusMessage;
		public string? StatusMessage
		{
			get => _statusMessage;
			private set
			{
				if (SetProperty(ref _statusMessage, value))
					OnPropertyChanged(nameof(HasStatusMessage));
			}
		}

		public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

		public ObservableCollection<ContributorViewModel> Contributors { get; } = new ObservableCollection<ContributorViewModel>();
		public bool HasContributors => Contributors.Count > 0;

		// Parameterless constructor for design-time
		public HomeViewModel()
		{
			_mainWindow = null!;
			RecentCombinations = new ObservableCollection<RecentCombinationItemViewModel>();
		}

		public HomeViewModel(MainWindowViewModel mainWindow)
		{
			_mainWindow = mainWindow;

			var recents = NyxAssetsEditor.Services.Persistence.PersistenceService.GetRecentCombinations();
			RecentCombinations = new ObservableCollection<RecentCombinationItemViewModel>();
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

			_ = LoadContributorsAsync();
			_ = CheckForUpdatesAsync();
		}

		private static readonly HttpClient _http = new HttpClient();
		private static readonly string _cacheDir = System.IO.Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"NyxAssetsEditor");
		private static readonly string _cacheFile = System.IO.Path.Combine(_cacheDir, "contributors_cache.json");
		private static readonly TimeSpan _cacheTtl = TimeSpan.FromDays(3);

		private async Task LoadContributorsAsync()
		{
			try
			{
				_http.DefaultRequestHeaders.UserAgent.TryParseAdd("NyxAssetsEditor");

				GithubContributor[]? items = null;

				// Try to read from cache first
				if (System.IO.File.Exists(_cacheFile))
				{
					var age = DateTime.UtcNow - System.IO.File.GetLastWriteTimeUtc(_cacheFile);
					if (age < _cacheTtl)
					{
						var cached = await System.IO.File.ReadAllTextAsync(_cacheFile);
						items = System.Text.Json.JsonSerializer.Deserialize<GithubContributor[]>(cached);
					}
				}

				// Fetch from GitHub if cache is missing or stale
				if (items == null)
				{
					var url = "https://api.github.com/repos/Tofame/NyxAssetsEditor/contributors?per_page=10&anon=false";
					items = await _http.GetFromJsonAsync<GithubContributor[]>(url);
					if (items != null)
					{
						System.IO.Directory.CreateDirectory(_cacheDir);
						var json = System.Text.Json.JsonSerializer.Serialize(items);
						await System.IO.File.WriteAllTextAsync(_cacheFile, json);
					}
				}

				if (items == null) return;

				foreach (var c in items)
				{
					// Avatar: fetched from GitHub CDN URL into memory only — never written to disk
					Bitmap? avatar = null;
					try
					{
						var bytes = await _http.GetByteArrayAsync(c.AvatarUrl + "&s=64");
						using var ms = new System.IO.MemoryStream(bytes);
						avatar = new Bitmap(ms);
					}
					catch { /* avatar stays null */ }

					Contributors.Add(new ContributorViewModel(c.Login, c.Contributions, c.HtmlUrl, avatar));
				}
				OnPropertyChanged(nameof(HasContributors));
			}
			catch { /* network unavailable – silently skip */ }
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

		public void RemoveCombination(RecentCombinationItemViewModel item)
		{
			NyxAssetsEditor.Services.Persistence.PersistenceService.RemoveRecentCombination(item.SpritePath, item.ThingsPath);
			RecentCombinations.Remove(item);
			OnPropertyChanged(nameof(FilteredRecentCombinations));
			OnPropertyChanged(nameof(HasFilteredCombinations));
		}

		public void NotifyMissingRecentCombination(RecentCombinationItemViewModel item, System.Collections.Generic.IReadOnlyList<string> missingPaths)
		{
			RemoveCombination(item);
			StatusMessage = missingPaths.Count == 1
				? $"Removed from recents — file not found:\n{missingPaths[0]}"
				: $"Removed from recents — files not found:\n{string.Join("\n", missingPaths)}";
		}

		public bool IsCombinationOpen(string spritePath, string thingsPath)
		{
			return _mainWindow?.IsCombinationOpen(spritePath, thingsPath) ?? false;
		}

		public string CurrentVersion
		{
			get
			{
				var version = typeof(HomeViewModel).Assembly.GetName().Version;
				if (version == null || (version.Major == 1 && version.Minor == 0 && version.Build == 0 && version.Revision == 0))
					return "0.0.0";
				return $"{version.Major}.{version.Minor}.{version.Build}";
			}
		}

		private string _latestVersion = "";
		public string LatestVersion
		{
			get => _latestVersion;
			set => SetProperty(ref _latestVersion, value);
		}

		private string _releaseUrl = "https://github.com/Tofame/NyxAssetsEditor/releases";
		public string ReleaseUrl
		{
			get => _releaseUrl;
			set => SetProperty(ref _releaseUrl, value);
		}

		private bool _isNewVersionAvailable;
		public bool IsNewVersionAvailable
		{
			get => _isNewVersionAvailable;
			set => SetProperty(ref _isNewVersionAvailable, value);
		}

		public void OpenReleaseWebsite()
		{
			try
			{
				var psi = new System.Diagnostics.ProcessStartInfo
				{
					FileName = ReleaseUrl,
					UseShellExecute = true
				};
				System.Diagnostics.Process.Start(psi);
			}
			catch { }
		}

		private async Task CheckForUpdatesAsync()
		{
			try
			{
				_http.DefaultRequestHeaders.UserAgent.TryParseAdd("NyxAssetsEditor");
				var url = "https://api.github.com/repos/Tofame/NyxAssetsEditor/releases/latest";
				var release = await _http.GetFromJsonAsync<GithubRelease>(url);
				if (release != null && !string.IsNullOrEmpty(release.TagName))
				{
					var latest = release.TagName.TrimStart('v', 'V');
					LatestVersion = latest;
					ReleaseUrl = release.HtmlUrl;

					if (Version.TryParse(latest, out var latestVer) && Version.TryParse(CurrentVersion, out var currentVer))
					{
						IsNewVersionAvailable = latestVer > currentVer;
					}
					else
					{
						IsNewVersionAvailable = latest != CurrentVersion && CurrentVersion == "0.0.0";
					}
				}
			}
			catch
			{
				// Keep app working without internet
			}
		}
	}

	file class GithubRelease
	{
		[JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
		[JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
	}

	public class ContributorViewModel
	{
		public string Login { get; }
		public int Contributions { get; }
		public string ProfileUrl { get; }
		public Bitmap? Avatar { get; }
		public string ContributionsLabel => $"{Contributions} commit{(Contributions == 1 ? "" : "s")}";

		public ContributorViewModel(string login, int contributions, string profileUrl, Bitmap? avatar)
		{
			Login = login;
			Contributions = contributions;
			ProfileUrl = profileUrl;
			Avatar = avatar;
		}
	}

	file class GithubContributor
	{
		[JsonPropertyName("login")] public string Login { get; set; } = "";
		[JsonPropertyName("avatar_url")] public string AvatarUrl { get; set; } = "";
		[JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
		[JsonPropertyName("contributions")] public int Contributions { get; set; }
	}
}
