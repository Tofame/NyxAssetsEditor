using System;
using NyxAssets.Things;
using NyxAssetsEditor.Services.Persistence;
using NyxAssetsEditor.Services.Rendering;
using NyxAssetsEditor.ViewModels.Core;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;

namespace NyxAssetsEditor.ViewModels.Pages
{
	public partial class SettingsViewModel : ViewModelBase
	{
		public string Title => "Application Settings";
		public string Description => "This is the dynamically loaded Settings View. Configure your editor options here.";

		public static int DefaultPageSize { get; private set; } = 100;
		public static int MaxRecentCombinations { get; private set; } = 10;
		public static int UndoLimit { get; private set; } = 10;

		private static int _defaultSpritePanelWidth = 430;
		public static int DefaultSpritePanelWidth
		{
			get => _defaultSpritePanelWidth;
			set
			{
				if (_defaultSpritePanelWidth != value)
				{
					_defaultSpritePanelWidth = value;
					NyxAssetsEditor.Services.Persistence.PersistenceService.SaveSettings();
				}
			}
		}

		private static int _defaultSpritePanelHeight = 500;
		public static int DefaultSpritePanelHeight
		{
			get => _defaultSpritePanelHeight;
			set
			{
				if (_defaultSpritePanelHeight != value)
				{
					_defaultSpritePanelHeight = value;
					NyxAssetsEditor.Services.Persistence.PersistenceService.SaveSettings();
				}
			}
		}

		private static int _defaultThingsPanelWidth = 430;
		public static int DefaultThingsPanelWidth
		{
			get => _defaultThingsPanelWidth;
			set
			{
				if (_defaultThingsPanelWidth != value)
				{
					_defaultThingsPanelWidth = value;
					NyxAssetsEditor.Services.Persistence.PersistenceService.SaveSettings();
				}
			}
		}

		private static int _defaultThingsPanelHeight = 500;
		public static int DefaultThingsPanelHeight
		{
			get => _defaultThingsPanelHeight;
			set
			{
				if (_defaultThingsPanelHeight != value)
				{
					_defaultThingsPanelHeight = value;
					NyxAssetsEditor.Services.Persistence.PersistenceService.SaveSettings();
				}
			}
		}

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

		private static bool _saveFloatingPanels = true;
		public static bool SaveFloatingPanels
		{
			get => _saveFloatingPanels;
			set
			{
				if (_saveFloatingPanels != value)
				{
					_saveFloatingPanels = value;
					NyxAssetsEditor.Services.Persistence.PersistenceService.SaveSettings();
				}
			}
		}

		private static bool _compileLinkedPairTogether = true;
		public static bool CompileLinkedPairTogether
		{
			get => _compileLinkedPairTogether;
			set
			{
				if (_compileLinkedPairTogether != value)
				{
					_compileLinkedPairTogether = value;
					NyxAssetsEditor.Services.Persistence.PersistenceService.SaveSettings();
				}
			}
		}

		public static event Action? ShowInformationBoxesChanged;

		private static bool _showInformationBoxes = true;
		public static bool ShowInformationBoxes
		{
			get => _showInformationBoxes;
			set
			{
				if (_showInformationBoxes != value)
				{
					_showInformationBoxes = value;
					ShowInformationBoxesChanged?.Invoke();
					NyxAssetsEditor.Services.Persistence.PersistenceService.SaveSettings();
				}
			}
		}

		private static string _customAccentColor = "";
		public static string CustomAccentColor
		{
			get => _customAccentColor;
			set
			{
				if (_customAccentColor != value)
				{
					_customAccentColor = value ?? "";
					ApplyAccentColor(_customAccentColor);
					NyxAssetsEditor.Services.Persistence.PersistenceService.SaveSettings();
				}
			}
		}

		public Avalonia.Media.Color ThemeColor
		{
			get
			{
				if (Avalonia.Application.Current?.Resources.TryGetValue("SystemAccentColor", out var res) == true)
				{
					if (res is Avalonia.Media.Color color)
						return color;
					if (res is Avalonia.Media.ISolidColorBrush brush)
						return brush.Color;
				}
				if (Avalonia.Application.Current?.PlatformSettings is { } settings)
				{
					try
					{
						return settings.GetColorValues().AccentColor1;
					}
					catch { }
				}
				return Avalonia.Media.Colors.DeepSkyBlue;
			}
			set
			{
				CustomAccentColor = value.ToString();
				OnPropertyChanged();
			}
		}

		public void ResetThemeColor()
		{
			CustomAccentColor = "";
			OnPropertyChanged(nameof(ThemeColor));
		}

		public static void ApplyAccentColor(string hexColor)
		{
			var resources = Avalonia.Application.Current?.Resources;
			if (resources == null) return;

			if (string.IsNullOrWhiteSpace(hexColor) || !Avalonia.Media.Color.TryParse(hexColor, out var color))
			{
				resources.Remove("SystemAccentColor");
				resources.Remove("SystemAccentColorLight1");
				resources.Remove("SystemAccentColorLight2");
				resources.Remove("SystemAccentColorLight3");
				resources.Remove("SystemAccentColorDark1");
				resources.Remove("SystemAccentColorDark2");
				resources.Remove("SystemAccentColorDark3");
				return;
			}

			resources["SystemAccentColor"] = color;
			resources["SystemAccentColorLight1"] = Lighten(color, 0.15f);
			resources["SystemAccentColorLight2"] = Lighten(color, 0.30f);
			resources["SystemAccentColorLight3"] = Lighten(color, 0.45f);
			resources["SystemAccentColorDark1"] = Darken(color, 0.15f);
			resources["SystemAccentColorDark2"] = Darken(color, 0.30f);
			resources["SystemAccentColorDark3"] = Darken(color, 0.45f);
		}

		private static Avalonia.Media.Color Lighten(Avalonia.Media.Color color, float amount)
		{
			return Avalonia.Media.Color.FromRgb(
				(byte)Math.Clamp(color.R + (255 - color.R) * amount, 0, 255),
				(byte)Math.Clamp(color.G + (255 - color.G) * amount, 0, 255),
				(byte)Math.Clamp(color.B + (255 - color.B) * amount, 0, 255));
		}

		private static Avalonia.Media.Color Darken(Avalonia.Media.Color color, float amount)
		{
			return Avalonia.Media.Color.FromRgb(
				(byte)Math.Clamp(color.R * (1 - amount), 0, 255),
				(byte)Math.Clamp(color.G * (1 - amount), 0, 255),
				(byte)Math.Clamp(color.B * (1 - amount), 0, 255));
		}

		private static bool _allowUnknownSignatures = true;
		public static bool AllowUnknownSignatures
		{
			get => _allowUnknownSignatures;
			set
			{
				if (_allowUnknownSignatures != value)
				{
					_allowUnknownSignatures = value;
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

		private static uint _clientVersion = 1098;
		public static uint ClientVersion
		{
			get => _clientVersion;
			set
			{
				if (_clientVersion != value)
				{
					_clientVersion = value;
					_selectedVersionIndex = System.Math.Max(0, NyxAssetsEditor.ViewModels.Common.ClientVersion.AvailableVersions.FindIndex(v => v.Version == value));
					ClientVersionChanged?.Invoke(value);
					NyxAssetsEditor.Services.Persistence.PersistenceService.SaveSettings();
				}
			}
		}


		private static uint _itemAnimationDurationMs = 500;
		private static uint _outfitAnimationDurationMs = 300;
		private static uint _effectAnimationDurationMs = 100;
		private static uint _missileAnimationDurationMs = 500;
		private static MountedOutfitAlignment _looktypeMountAlignment = MountedOutfitAlignment.OtClientCompatible;
		private static int _looktypeMountedRiderOffsetX;
		private static int _looktypeMountedRiderOffsetY;

		private static string _thingEditorGridColor = "#B4808080";
		private static int _thingEditorGridLineWidth = 1;
		private static string _thingEditorDragGridColor = "#B4FF69B4";
		private static int _thingEditorDragGridLineWidth = 1;
		private static string _thingEditorDragHighlightColor = "#803A7BD5";

		public static event Action? ThingEditorAppearanceSettingsChanged;
		public static event Action? LooktypeRendererSettingsChanged;

		public static MountedOutfitAlignment LooktypeMountAlignment => _looktypeMountAlignment;
		public static int LooktypeMountedRiderOffsetX => _looktypeMountedRiderOffsetX;
		public static int LooktypeMountedRiderOffsetY => _looktypeMountedRiderOffsetY;

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

		public int SelectedLooktypeMountAlignmentIndex
		{
			get => (int)_looktypeMountAlignment;
			set
			{
				var alignment = value == (int)MountedOutfitAlignment.IndependentAssetDisplacement
					? MountedOutfitAlignment.IndependentAssetDisplacement
					: MountedOutfitAlignment.OtClientCompatible;
				if (_looktypeMountAlignment == alignment) return;
				_looktypeMountAlignment = alignment;
				OnPropertyChanged();
				LooktypeRendererSettingsChanged?.Invoke();
				PersistenceService.SaveSettings();
			}
		}

		public int LooktypeMountedRiderOffsetXSetting
		{
			get => _looktypeMountedRiderOffsetX;
			set => SetLooktypeMountedRiderOffset(ref _looktypeMountedRiderOffsetX, value, nameof(LooktypeMountedRiderOffsetXSetting));
		}

		public int LooktypeMountedRiderOffsetYSetting
		{
			get => _looktypeMountedRiderOffsetY;
			set => SetLooktypeMountedRiderOffset(ref _looktypeMountedRiderOffsetY, value, nameof(LooktypeMountedRiderOffsetYSetting));
		}

		public bool SaveFloatingPanelsSetting
		{
			get => SaveFloatingPanels;
			set
			{
				if (SaveFloatingPanels != value)
				{
					SaveFloatingPanels = value;
					OnPropertyChanged();
				}
			}
		}

		public bool PreloadGraphicalAssetsSetting
		{
			get => PreloadGraphicalAssets;
			set => PreloadGraphicalAssets = value;
		}

		public bool AllowUnknownSignaturesSetting
		{
			get => AllowUnknownSignatures;
			set => AllowUnknownSignatures = value;
		}

		public bool CompileLinkedPairTogetherSetting
		{
			get => CompileLinkedPairTogether;
			set
			{
				if (CompileLinkedPairTogether != value)
				{
					CompileLinkedPairTogether = value;
					OnPropertyChanged();
				}
			}
		}

		public bool ShowInformationBoxesSetting
		{
			get => ShowInformationBoxes;
			set
			{
				if (ShowInformationBoxes != value)
				{
					ShowInformationBoxes = value;
					OnPropertyChanged();
				}
			}
		}

		public int DefaultSpritePanelWidthSetting
		{
			get => DefaultSpritePanelWidth;
			set
			{
				if (DefaultSpritePanelWidth != value)
				{
					DefaultSpritePanelWidth = value;
					OnPropertyChanged();
				}
			}
		}

		public int DefaultSpritePanelHeightSetting
		{
			get => DefaultSpritePanelHeight;
			set
			{
				if (DefaultSpritePanelHeight != value)
				{
					DefaultSpritePanelHeight = value;
					OnPropertyChanged();
				}
			}
		}

		public int DefaultThingsPanelWidthSetting
		{
			get => DefaultThingsPanelWidth;
			set
			{
				if (DefaultThingsPanelWidth != value)
				{
					DefaultThingsPanelWidth = value;
					OnPropertyChanged();
				}
			}
		}

		public int DefaultThingsPanelHeightSetting
		{
			get => DefaultThingsPanelHeight;
			set
			{
				if (DefaultThingsPanelHeight != value)
				{
					DefaultThingsPanelHeight = value;
					OnPropertyChanged();
				}
			}
		}

		private static bool _addonDuplicateFrameEnabled = false;
		public static bool AddonDuplicateFrameEnabled
		{
			get => _addonDuplicateFrameEnabled;
			set
			{
				if (_addonDuplicateFrameEnabled == value) return;
				_addonDuplicateFrameEnabled = value;
				AddonSettingsChanged?.Invoke();
				PersistenceService.SaveSettings();
			}
		}

		private static bool _addonRotateCloneDirectionEnabled = false;
		public static bool AddonRotateCloneDirectionEnabled
		{
			get => _addonRotateCloneDirectionEnabled;
			set
			{
				if (_addonRotateCloneDirectionEnabled == value) return;
				_addonRotateCloneDirectionEnabled = value;
				AddonSettingsChanged?.Invoke();
				PersistenceService.SaveSettings();
			}
		}

		private static bool _allowRelocatingDirection = false;
		public static bool AllowRelocatingDirection
		{
			get => _allowRelocatingDirection;
			set
			{
				if (_allowRelocatingDirection == value) return;
				_allowRelocatingDirection = value;
				AddonSettingsChanged?.Invoke();
				PersistenceService.SaveSettings();
			}
		}

		public static event Action? AddonSettingsChanged;

		public enum LaunchSection
		{
			Home,
			Assets,
			Paint,
			Converter
		}

		private static LaunchSection _defaultLaunchSection = LaunchSection.Home;
		public static LaunchSection DefaultLaunchSection
		{
			get => _defaultLaunchSection;
			set
			{
				if (_defaultLaunchSection == value) return;
				_defaultLaunchSection = value;
				PersistenceService.SaveSettings();
			}
		}

		private static string _lastAssetExportFormat = "png";
		public static string LastAssetExportFormat
		{
			get => _lastAssetExportFormat;
			private set => _lastAssetExportFormat = value;
		}

		private static string _lastAssetExportDirectory = "";
		public static string LastAssetExportDirectory
		{
			get => _lastAssetExportDirectory;
			private set => _lastAssetExportDirectory = value;
		}

		private static bool _lastThingExportSkipWest;
		public static bool LastThingExportSkipWest
		{
			get => _lastThingExportSkipWest;
			private set => _lastThingExportSkipWest = value;
		}

		private static bool _thingEditorShowAllDirections;
		public static bool ThingEditorShowAllDirections
		{
			get => _thingEditorShowAllDirections;
			set
			{
				if (_thingEditorShowAllDirections == value) return;
				_thingEditorShowAllDirections = value;
				PersistenceService.SaveSettings();
			}
		}

		private static bool _thingEditorShowTimeframe;
		public static bool ThingEditorShowTimeframe
		{
			get => _thingEditorShowTimeframe;
			set
			{
				if (_thingEditorShowTimeframe == value) return;
				_thingEditorShowTimeframe = value;
				PersistenceService.SaveSettings();
			}
		}

		private static bool _thingEditorAutoRotate;
		public static bool ThingEditorAutoRotate
		{
			get => _thingEditorAutoRotate;
			set
			{
				if (_thingEditorAutoRotate == value) return;
				_thingEditorAutoRotate = value;
				PersistenceService.SaveSettings();
			}
		}

		private static int _thingEditorRotateSpeedMs = 500;
		public static int ThingEditorRotateSpeedMs
		{
			get => _thingEditorRotateSpeedMs;
			set
			{
				var clamped = Math.Clamp(value, 50, 5000);
				if (_thingEditorRotateSpeedMs == clamped) return;
				_thingEditorRotateSpeedMs = clamped;
				PersistenceService.SaveSettings();
			}
		}

		public static string NormalizeAssetExportFormat(string? format, bool thingsFormats)
		{
			var normalized = (format ?? "png").Trim().ToLowerInvariant();
			if (normalized is "png" or "bmp" or "jpg" or "jpeg")
				return normalized == "jpeg" ? "jpg" : normalized;
			if (thingsFormats && normalized is "obd" or "nyx-thing")
				return normalized;
			return "png";
		}

		public static void RememberAssetExport(string format, string directory, bool skipWest, bool thingsFormats)
		{
			format = NormalizeAssetExportFormat(format, thingsFormats);
			var changed = _lastAssetExportFormat != format
				|| _lastThingExportSkipWest != skipWest
				|| (!string.IsNullOrWhiteSpace(directory) && _lastAssetExportDirectory != directory);
			_lastAssetExportFormat = format;
			if (!string.IsNullOrWhiteSpace(directory))
				_lastAssetExportDirectory = directory;
			if (thingsFormats)
				_lastThingExportSkipWest = skipWest;
			if (changed)
				PersistenceService.SaveSettings();
		}

		public int SelectedDefaultLaunchSectionIndex
		{
			get => (int)DefaultLaunchSection;
			set
			{
				if ((int)DefaultLaunchSection != value)
				{
					DefaultLaunchSection = (LaunchSection)value;
					OnPropertyChanged();
				}
			}
		}

		private static bool _offsetPreviewCenterOutfits = false;
		public static event Action? OffsetPreviewSettingsChanged;

		public static bool OffsetPreviewCenterOutfits
		{
			get => _offsetPreviewCenterOutfits;
			set
			{
				if (_offsetPreviewCenterOutfits == value)
					return;
				_offsetPreviewCenterOutfits = value;
				OffsetPreviewSettingsChanged?.Invoke();
				PersistenceService.SaveSettings();
			}
		}

		public bool OffsetPreviewCenterOutfitsSetting
		{
			get => OffsetPreviewCenterOutfits;
			set
			{
				if (OffsetPreviewCenterOutfits != value)
				{
					OffsetPreviewCenterOutfits = value;
					OnPropertyChanged();
				}
			}
		}

		public bool AddonDuplicateFrameEnabledSetting
		{
			get => AddonDuplicateFrameEnabled;
			set
			{
				if (AddonDuplicateFrameEnabled != value)
				{
					AddonDuplicateFrameEnabled = value;
					OnPropertyChanged();
				}
			}
		}

		public bool AddonRotateCloneDirectionEnabledSetting
		{
			get => AddonRotateCloneDirectionEnabled;
			set
			{
				if (AddonRotateCloneDirectionEnabled != value)
				{
					AddonRotateCloneDirectionEnabled = value;
					OnPropertyChanged();
				}
			}
		}

		public bool AllowRelocatingDirectionSetting
		{
			get => AllowRelocatingDirection;
			set
			{
				if (AllowRelocatingDirection != value)
				{
					AllowRelocatingDirection = value;
					OnPropertyChanged();
				}
			}
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
			string? thingEditorDragHighlightColor = null,
			int maxRecentCombinations = 10,
			int undoLimit = 10,
			bool allowUnknownSignatures = true,
			string? looktypeMountAlignment = null,
			int looktypeMountedRiderOffsetX = 0,
			int looktypeMountedRiderOffsetY = 0,
			bool compileLinkedPairTogether = true,
			string? customAccentColor = null,
			bool showInformationBoxes = true,
			int defaultSpritePanelWidth = 430,
			int defaultSpritePanelHeight = 500,
			int defaultThingsPanelWidth = 430,
			int defaultThingsPanelHeight = 500,
			bool saveFloatingPanels = true,
			bool offsetPreviewCenterOutfits = false,
			bool addonDuplicateFrameEnabled = false,
			bool addonRotateCloneDirectionEnabled = false,
			bool allowRelocatingDirection = false,
			LaunchSection defaultLaunchSection = LaunchSection.Home,
			string? lastAssetExportFormat = null,
			string? lastAssetExportDirectory = null,
			bool lastThingExportSkipWest = false,
			bool thingEditorShowAllDirections = false,
			bool thingEditorShowTimeframe = false,
			bool thingEditorAutoRotate = false,
			int thingEditorRotateSpeedMs = 500)
		{
			DefaultPageSize = defaultPageSize;
			MaxRecentCombinations = maxRecentCombinations;
			UndoLimit = undoLimit;
			_useTransparentPixels = useTransparentPixels;
			_useExtendedSpriteIds = useExtendedSpriteIds;
			_preloadGraphicalAssets = preloadGraphicalAssets;
			_allowUnknownSignatures = allowUnknownSignatures;
			_compileLinkedPairTogether = compileLinkedPairTogether;
			_showInformationBoxes = showInformationBoxes;
			_customAccentColor = customAccentColor ?? "";
			ApplyAccentColor(_customAccentColor);
			_assetDisplaySize = assetDisplaySize;
			ThingIdOffset = thingIdOffset;
			_clientVersion = clientVersion;
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
			_looktypeMountAlignment = Enum.TryParse<MountedOutfitAlignment>(looktypeMountAlignment, true, out var alignment) &&
				Enum.IsDefined(typeof(MountedOutfitAlignment), alignment)
					? alignment
					: MountedOutfitAlignment.OtClientCompatible;
			_looktypeMountedRiderOffsetX = Math.Clamp(looktypeMountedRiderOffsetX, -128, 128);
			_looktypeMountedRiderOffsetY = Math.Clamp(looktypeMountedRiderOffsetY, -128, 128);
			_defaultSpritePanelWidth = defaultSpritePanelWidth;
			_defaultSpritePanelHeight = defaultSpritePanelHeight;
			_defaultThingsPanelWidth = defaultThingsPanelWidth;
			_defaultThingsPanelHeight = defaultThingsPanelHeight;
			_saveFloatingPanels = saveFloatingPanels;
			_offsetPreviewCenterOutfits = offsetPreviewCenterOutfits;
			_addonDuplicateFrameEnabled = addonDuplicateFrameEnabled;
			_addonRotateCloneDirectionEnabled = addonRotateCloneDirectionEnabled;
			_allowRelocatingDirection = allowRelocatingDirection;
			_defaultLaunchSection = defaultLaunchSection;
			_lastAssetExportFormat = NormalizeAssetExportFormat(lastAssetExportFormat, thingsFormats: true);
			_lastAssetExportDirectory = lastAssetExportDirectory ?? "";
			_lastThingExportSkipWest = lastThingExportSkipWest;
			_thingEditorShowAllDirections = thingEditorShowAllDirections;
			_thingEditorShowTimeframe = thingEditorShowTimeframe;
			_thingEditorAutoRotate = thingEditorAutoRotate;
			_thingEditorRotateSpeedMs = Math.Clamp(thingEditorRotateSpeedMs, 50, 5000);
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
					OnPropertyChanged(nameof(SelectedVersionIndex));
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
					if (value >= 0 && value < AvailableVersions.Count)
					{
						ClientVersion = AvailableVersions[value].Version;
						OnPropertyChanged(nameof(SelectedVersion));
						OnPropertyChanged(nameof(SelectedVersionIndex));
					}
				}
			}
		}

		private int _selectedMaxRecentCombinationsIndex = 3; // Index 3 maps to 10

		public int SelectedMaxRecentCombinationsIndex
		{
			get => _selectedMaxRecentCombinationsIndex;
			set
			{
				if (_selectedMaxRecentCombinationsIndex != value)
				{
					_selectedMaxRecentCombinationsIndex = value;
					OnPropertyChanged(nameof(SelectedMaxRecentCombinationsIndex));

					int newMax = value switch
					{
						0 => 4,
						1 => 6,
						2 => 8,
						3 => 10,
						4 => 16,
						5 => 20,
						_ => 10
					};
					MaxRecentCombinations = newMax;
					NyxAssetsEditor.Services.Persistence.PersistenceService.SaveSettings();
				}
			}
		}

		private int _selectedUndoLimitIndex = 1; // Index 1 maps to 10

		public int SelectedUndoLimitIndex
		{
			get => _selectedUndoLimitIndex;
			set
			{
				if (_selectedUndoLimitIndex != value)
				{
					_selectedUndoLimitIndex = value;
					OnPropertyChanged(nameof(SelectedUndoLimitIndex));

					int newLimit = value switch
					{
						0 => 5,
						1 => 10,
						2 => 15,
						3 => 20,
						_ => 10
					};
					UndoLimit = newLimit;
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
						3 => 500,
						4 => 1000,
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

		private void SetLooktypeMountedRiderOffset(ref int field, int value, string propertyName)
		{
			var clamped = Math.Clamp(value, -128, 128);
			if (field == clamped) return;
			field = clamped;
			OnPropertyChanged(propertyName);
			LooktypeRendererSettingsChanged?.Invoke();
			PersistenceService.SaveSettings();
		}

		[RelayCommand]
		private void SyncSpritesSizeToThings()
		{
			DefaultThingsPanelWidth = DefaultSpritePanelWidth;
			DefaultThingsPanelHeight = DefaultSpritePanelHeight;
			OnPropertyChanged(nameof(DefaultThingsPanelWidthSetting));
			OnPropertyChanged(nameof(DefaultThingsPanelHeightSetting));
		}

		[RelayCommand]
		private void SyncThingsSizeToSprites()
		{
			DefaultSpritePanelWidth = DefaultThingsPanelWidth;
			DefaultSpritePanelHeight = DefaultThingsPanelHeight;
			OnPropertyChanged(nameof(DefaultSpritePanelWidthSetting));
			OnPropertyChanged(nameof(DefaultSpritePanelHeightSetting));
		}

		public SettingsViewModel()
		{
			_selectedPageIndex = DefaultPageSize switch
			{
				50 => 0,
				100 => 1,
				200 => 2,
				500 => 3,
				1000 => 4,
				_ => 1
			};
			_selectedVersionIndex = System.Math.Max(0, AvailableVersions.FindIndex(v => v.Version == ClientVersion));
			_selectedDisplaySizeIndex = AssetDisplaySize switch
			{
				32 => 0,
				64 => 1,
				96 => 2,
				128 => 3,
				_ => 0
			};
			_selectedMaxRecentCombinationsIndex = MaxRecentCombinations switch
			{
				4 => 0,
				6 => 1,
				8 => 2,
				10 => 3,
				16 => 4,
				20 => 5,
				_ => 3
			};
			_selectedUndoLimitIndex = UndoLimit switch
			{
				5 => 0,
				10 => 1,
				15 => 2,
				20 => 3,
				_ => 1
			};
		}
	}
}
