using System;
using NyxAssets.Things;
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

		private static bool _preloadGraphicalAssets = true;
		public static bool PreloadGraphicalAssets
		{
			get => _preloadGraphicalAssets;
			set
			{
				if (_preloadGraphicalAssets != value)
				{
					_preloadGraphicalAssets = value;
					NyxAssetsEditor.Services.Persistence.PersistenceService.SaveSettings();
				}
			}
		}

		private static int _assetDisplaySize = 32;
		public static int AssetDisplaySize
		{
			get => _assetDisplaySize;
			set
			{
				if (_assetDisplaySize != value)
				{
					_assetDisplaySize = value;
					AssetDisplaySizeChanged?.Invoke(value);
					NyxAssetsEditor.Services.Persistence.PersistenceService.SaveSettings();
				}
			}
		}

		public static uint ThingIdOffset { get; set; } = 0;
		public static uint ClientVersion { get; set; } = 1098;

		private static uint _itemAnimationDurationMs = 500;
		private static uint _outfitAnimationDurationMs = 300;
		private static uint _effectAnimationDurationMs = 100;
		private static uint _missileAnimationDurationMs = 500;

		private static string _thingEditorGridColor = "#B4808080";
		private static int _thingEditorGridLineWidth = 1;
		private static string _thingEditorDragGridColor = "#B4FF69B4";
		private static int _thingEditorDragGridLineWidth = 1;
		private static string _thingEditorDragHighlightColor = "#803A7BD5";

		public static event Action? ThingEditorAppearanceSettingsChanged;

		public static uint ItemAnimationDurationMs
		{
			get => _itemAnimationDurationMs;
			set => _itemAnimationDurationMs = Math.Max(0, value);
		}

		public static uint OutfitAnimationDurationMs
		{
			get => _outfitAnimationDurationMs;
			set => _outfitAnimationDurationMs = Math.Max(0, value);
		}

		public static uint EffectAnimationDurationMs
		{
			get => _effectAnimationDurationMs;
			set => _effectAnimationDurationMs = Math.Max(0, value);
		}

		public static uint MissileAnimationDurationMs
		{
			get => _missileAnimationDurationMs;
			set => _missileAnimationDurationMs = Math.Max(0, value);
		}

		public static uint GetDefaultAnimationDurationMs(ThingKind kind) => kind switch
		{
			ThingKind.Outfit => OutfitAnimationDurationMs,
			ThingKind.Effect => EffectAnimationDurationMs,
			ThingKind.Missile => MissileAnimationDurationMs,
			_ => ItemAnimationDurationMs,
		};

		public static string ThingEditorGridColor
		{
			get => _thingEditorGridColor;
			set
			{
				if (_thingEditorGridColor == value)
					return;
				_thingEditorGridColor = value;
				ThingEditorAppearanceSettingsChanged?.Invoke();
				PersistenceService.SaveSettings();
			}
		}

		public static int ThingEditorGridLineWidth
		{
			get => _thingEditorGridLineWidth;
			set
			{
				var clamped = Math.Clamp(value, 1, 4);
				if (_thingEditorGridLineWidth == clamped)
					return;
				_thingEditorGridLineWidth = clamped;
				ThingEditorAppearanceSettingsChanged?.Invoke();
				PersistenceService.SaveSettings();
			}
		}

		public static string ThingEditorDragGridColor
		{
			get => _thingEditorDragGridColor;
			set
			{
				if (_thingEditorDragGridColor == value)
					return;
				_thingEditorDragGridColor = value;
				ThingEditorAppearanceSettingsChanged?.Invoke();
				PersistenceService.SaveSettings();
			}
		}

		public static int ThingEditorDragGridLineWidth
		{
			get => _thingEditorDragGridLineWidth;
			set
			{
				var clamped = Math.Clamp(value, 1, 4);
				if (_thingEditorDragGridLineWidth == clamped)
					return;
				_thingEditorDragGridLineWidth = clamped;
				ThingEditorAppearanceSettingsChanged?.Invoke();
				PersistenceService.SaveSettings();
			}
		}

		public static string ThingEditorDragHighlightColor
		{
			get => _thingEditorDragHighlightColor;
			set
			{
				if (_thingEditorDragHighlightColor == value)
					return;
				_thingEditorDragHighlightColor = value;
				ThingEditorAppearanceSettingsChanged?.Invoke();
				PersistenceService.SaveSettings();
			}
		}

		public string ThingEditorGridColorSetting
		{
			get => ThingEditorGridColor;
			set => ThingEditorGridColor = value;
		}

		public int ThingEditorGridLineWidthSetting
		{
			get => ThingEditorGridLineWidth;
			set => ThingEditorGridLineWidth = value;
		}

		public string ThingEditorDragGridColorSetting
		{
			get => ThingEditorDragGridColor;
			set => ThingEditorDragGridColor = value;
		}

		public int ThingEditorDragGridLineWidthSetting
		{
			get => ThingEditorDragGridLineWidth;
			set => ThingEditorDragGridLineWidth = value;
		}

		public string ThingEditorDragHighlightColorSetting
		{
			get => ThingEditorDragHighlightColor;
			set => ThingEditorDragHighlightColor = value;
		}

		public bool PreloadGraphicalAssetsSetting
		{
			get => PreloadGraphicalAssets;
			set => PreloadGraphicalAssets = value;
		}

		public static event Action<int>? DefaultPageSizeChanged;
		public static event Action<uint>? ThingIdOffsetChanged;
		public static event Action<uint>? ClientVersionChanged;
		public static event Action<int>? AssetDisplaySizeChanged;

		public static void SetSettings(
			int defaultPageSize,
			bool useTransparentPixels,
			bool useExtendedSpriteIds,
			uint thingIdOffset,
			uint clientVersion,
			bool preloadGraphicalAssets = true,
			int assetDisplaySize = 32,
			uint itemAnimationDurationMs = 500,
			uint outfitAnimationDurationMs = 300,
			uint effectAnimationDurationMs = 100,
			uint missileAnimationDurationMs = 500,
			string? thingEditorGridColor = null,
			int thingEditorGridLineWidth = 1,
			string? thingEditorDragGridColor = null,
			int thingEditorDragGridLineWidth = 1,
			string? thingEditorDragHighlightColor = null)
		{
			DefaultPageSize = defaultPageSize;
			_useTransparentPixels = useTransparentPixels;
			_useExtendedSpriteIds = useExtendedSpriteIds;
			_preloadGraphicalAssets = preloadGraphicalAssets;
			_assetDisplaySize = assetDisplaySize;
			ThingIdOffset = thingIdOffset;
			ClientVersion = clientVersion;
			_selectedVersionIndex = System.Math.Max(0, NyxAssetsEditor.ViewModels.Common.ClientVersion.AvailableVersions.FindIndex(v => v.Version == clientVersion));
			ItemAnimationDurationMs = itemAnimationDurationMs;
			OutfitAnimationDurationMs = outfitAnimationDurationMs;
			EffectAnimationDurationMs = effectAnimationDurationMs;
			MissileAnimationDurationMs = missileAnimationDurationMs;
			if (!string.IsNullOrWhiteSpace(thingEditorGridColor))
				_thingEditorGridColor = thingEditorGridColor;
			_thingEditorGridLineWidth = Math.Clamp(thingEditorGridLineWidth, 1, 4);
			if (!string.IsNullOrWhiteSpace(thingEditorDragGridColor))
				_thingEditorDragGridColor = thingEditorDragGridColor;
			_thingEditorDragGridLineWidth = Math.Clamp(thingEditorDragGridLineWidth, 1, 4);
			if (!string.IsNullOrWhiteSpace(thingEditorDragHighlightColor))
				_thingEditorDragHighlightColor = thingEditorDragHighlightColor;
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

		public System.Collections.Generic.List<NyxAssetsEditor.ViewModels.Common.ClientVersion> AvailableVersions => NyxAssetsEditor.ViewModels.Common.ClientVersion.AvailableVersions;

		public NyxAssetsEditor.ViewModels.Common.ClientVersion SelectedVersion
		{
			get
			{
				var found = AvailableVersions.Find(v => v.Version == ClientVersion);
				return found ?? AvailableVersions[0];
			}
			set
			{
				if (value != null && ClientVersion != value.Version)
				{
					ClientVersion = value.Version;
					OnPropertyChanged(nameof(SelectedVersion));

					int idx = AvailableVersions.IndexOf(value);
					if (idx >= 0 && _selectedVersionIndex != idx)
					{
						_selectedVersionIndex = idx;
						OnPropertyChanged(nameof(SelectedVersionIndex));
					}

					ClientVersionChanged?.Invoke(ClientVersion);
					NyxAssetsEditor.Services.Persistence.PersistenceService.SaveSettings();
				}
			}
		}

		private static int _selectedVersionIndex = 0;

		public int SelectedVersionIndex
		{
			get => _selectedVersionIndex;
			set
			{
				if (_selectedVersionIndex != value)
				{
					_selectedVersionIndex = value;
					OnPropertyChanged(nameof(SelectedVersionIndex));

					if (value >= 0 && value < AvailableVersions.Count)
					{
						ClientVersion = AvailableVersions[value].Version;
						OnPropertyChanged(nameof(SelectedVersion));
						ClientVersionChanged?.Invoke(ClientVersion);
						NyxAssetsEditor.Services.Persistence.PersistenceService.SaveSettings();
					}
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

		private int _selectedDisplaySizeIndex = 0; // Index 0 maps to 32

		public int SelectedDisplaySizeIndex
		{
			get => _selectedDisplaySizeIndex;
			set
			{
				if (_selectedDisplaySizeIndex != value)
				{
					_selectedDisplaySizeIndex = value;
					OnPropertyChanged(nameof(SelectedDisplaySizeIndex));

					AssetDisplaySize = value switch
					{
						0 => 32,
						1 => 64,
						2 => 96,
						3 => 128,
						_ => 32
					};
				}
			}
		}

		public int ItemAnimationDuration
		{
			get => (int)ItemAnimationDurationMs;
			set => SetAnimationDuration(ref _itemAnimationDurationMs, value, nameof(ItemAnimationDuration));
		}

		public int OutfitAnimationDuration
		{
			get => (int)OutfitAnimationDurationMs;
			set => SetAnimationDuration(ref _outfitAnimationDurationMs, value, nameof(OutfitAnimationDuration));
		}

		public int EffectAnimationDuration
		{
			get => (int)EffectAnimationDurationMs;
			set => SetAnimationDuration(ref _effectAnimationDurationMs, value, nameof(EffectAnimationDuration));
		}

		public int MissileAnimationDuration
		{
			get => (int)MissileAnimationDurationMs;
			set => SetAnimationDuration(ref _missileAnimationDurationMs, value, nameof(MissileAnimationDuration));
		}

		private void SetAnimationDuration(ref uint field, int value, string propertyName)
		{
			var clamped = value < 0 ? 0u : (uint)value;
			if (field == clamped)
				return;

			field = clamped;
			OnPropertyChanged(propertyName);
			PersistenceService.SaveSettings();
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
			_selectedDisplaySizeIndex = AssetDisplaySize switch
			{
				32 => 0,
				64 => 1,
				96 => 2,
				128 => 3,
				_ => 0
			};
		}
	}
}
