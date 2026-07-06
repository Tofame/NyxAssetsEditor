using System;
using NyxAssetsEditor.Services.Persistence;
using NyxAssetsEditor.ViewModels.Core;

namespace NyxAssetsEditor.ViewModels.Pages
{
	public class SettingsViewModel : ViewModelBase
	{
		public string Title => "Application Settings";
		public string Description => "This is the dynamically loaded Settings View. Configure your editor options here.";

		public static int DefaultPageSize { get; private set; } = 100;

		private static bool _useTransparentPixels = true;
		public static bool UseTransparentPixels
		{
			get => _useTransparentPixels;
			set
			{
				if (_useTransparentPixels != value)
				{
					_useTransparentPixels = value;
					NyxAssetsEditor.Services.Persistence.PersistenceService.SaveSettings();
				}
			}
		}

		private static bool _useExtendedSpriteIds = true;
		public static bool UseExtendedSpriteIds
		{
			get => _useExtendedSpriteIds;
			set
			{
				if (_useExtendedSpriteIds != value)
				{
					_useExtendedSpriteIds = value;
					NyxAssetsEditor.Services.Persistence.PersistenceService.SaveSettings();
				}
			}
		}

		public static uint ThingIdOffset { get; set; } = 0;
		public static uint ClientVersion { get; set; } = 1098;
		public static event Action<int>? DefaultPageSizeChanged;
		public static event Action<uint>? ThingIdOffsetChanged;
		public static event Action<uint>? ClientVersionChanged;

		public static void SetSettings(int defaultPageSize, bool useTransparentPixels, bool useExtendedSpriteIds, uint thingIdOffset, uint clientVersion)
		{
			DefaultPageSize = defaultPageSize;
			_useTransparentPixels = useTransparentPixels;
			_useExtendedSpriteIds = useExtendedSpriteIds;
			ThingIdOffset = thingIdOffset;
			ClientVersion = clientVersion;
		}

		public int SelectedThingIdOffset
		{
			get => (int)ThingIdOffset;
			set
			{
				uint uValue = value < 0 ? 0 : (uint)value;
				if (ThingIdOffset != uValue)
				{
					ThingIdOffset = uValue;
					OnPropertyChanged(nameof(SelectedThingIdOffset));
					ThingIdOffsetChanged?.Invoke(uValue);
					NyxAssetsEditor.Services.Persistence.PersistenceService.SaveSettings();
				}
			}
		}

		private int _selectedVersionIndex = 0; // Index 0 maps to 10.98

		public int SelectedVersionIndex
		{
			get => _selectedVersionIndex;
			set
			{
				if (_selectedVersionIndex != value)
				{
					_selectedVersionIndex = value;
					OnPropertyChanged(nameof(SelectedVersionIndex));

					ClientVersion = value switch
					{
						0 => 1098,
						1 => 860,
						2 => 760,
						_ => 1098
					};
					ClientVersionChanged?.Invoke(ClientVersion);
					NyxAssetsEditor.Services.Persistence.PersistenceService.SaveSettings();
				}
			}
		}

		private int _selectedPageIndex = 1; // Index 1 maps to 100

		public int SelectedPageSizeIndex
		{
			get => _selectedPageIndex;
			set
			{
				if (_selectedPageIndex != value)
				{
					_selectedPageIndex = value;
					OnPropertyChanged(nameof(SelectedPageSizeIndex));

					int newSize = value switch
					{
						0 => 50,
						1 => 100,
						2 => 200,
						_ => 100
					};
					DefaultPageSize = newSize;
					DefaultPageSizeChanged?.Invoke(newSize);
					NyxAssetsEditor.Services.Persistence.PersistenceService.SaveSettings();
				}
			}
		}

		public SettingsViewModel()
		{
			_selectedPageIndex = DefaultPageSize switch
			{
				50 => 0,
				100 => 1,
				200 => 2,
				_ => 1
			};
			_selectedVersionIndex = ClientVersion switch
			{
				1098 => 0,
				860 => 1,
				760 => 2,
				_ => 0
			};
		}
	}
}
