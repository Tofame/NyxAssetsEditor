using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using Tomlyn;
using NyxAssetsEditor.Services.Rendering;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;
using NyxAssetsEditor.ViewModels.Core;
using NyxAssetsEditor.ViewModels.Pages;
using System.Linq;

namespace NyxAssetsEditor.Services.Persistence
{
	public static class PersistenceService
	{
		private static readonly string SettingsPath = Path.Combine(AppContext.BaseDirectory, "settings.toml");
		private static readonly string AppStatePath = Path.Combine(AppContext.BaseDirectory, "app_state.toml");
		private static readonly string FloatingSaveDataPath = Path.Combine(AppContext.BaseDirectory, "floating_save_data.toml");

		public class FloatingStateTomlModel
		{
			public List<PanelStateModel> Panels { get; set; } = new List<PanelStateModel>();
		}

		private static bool _isRestoring;
		private static SlicerStateModel _slicerState = new();

		static PersistenceService()
		{
			try
			{
				string tempUndoDir = Path.Combine(AppContext.BaseDirectory, "temp_undo");
				if (Directory.Exists(tempUndoDir))
				{
					Directory.Delete(tempUndoDir, true);
				}
			}
			catch
			{
				// Ignore
			}
		}

		public class SettingsTomlModel
		{
			public int DefaultPageSize { get; set; } = 100;
			public bool UseTransparentPixels { get; set; } = true;
			public bool UseExtendedSpriteIds { get; set; } = true;
			public bool PreloadGraphicalAssets { get; set; } = true;
			public int AssetDisplaySize { get; set; } = 32;
			public uint ThingIdOffset { get; set; } = 0;
			public uint ClientVersion { get; set; } = 1098;
			public uint ItemAnimationDurationMs { get; set; } = 500;
			public uint OutfitAnimationDurationMs { get; set; } = 300;
			public uint EffectAnimationDurationMs { get; set; } = 100;
			public uint MissileAnimationDurationMs { get; set; } = 500;
			public string LooktypeMountAlignment { get; set; } = nameof(MountedOutfitAlignment.OtClientCompatible);
			public int LooktypeMountedRiderOffsetX { get; set; }
			public int LooktypeMountedRiderOffsetY { get; set; }
			public string ThingEditorGridColor { get; set; } = "#B4808080";
			public int ThingEditorGridLineWidth { get; set; } = 1;
			public string ThingEditorDragGridColor { get; set; } = "#B4FF69B4";
			public int ThingEditorDragGridLineWidth { get; set; } = 1;
			public string ThingEditorDragHighlightColor { get; set; } = "#803A7BD5";
			public int MaxRecentCombinations { get; set; } = 10;
			public int UndoLimit { get; set; } = 10;
			public bool AllowUnknownSignatures { get; set; } = true;
			public bool CompileLinkedPairTogether { get; set; } = true;
			public bool ShowInformationBoxes { get; set; } = true;
			public string CustomAccentColor { get; set; } = "";
			public int DefaultSpritePanelWidth { get; set; } = 430;
			public int DefaultSpritePanelHeight { get; set; } = 500;
			public int DefaultThingsPanelWidth { get; set; } = 430;
			public int DefaultThingsPanelHeight { get; set; } = 500;
			public bool SaveFloatingPanels { get; set; } = true;
			public bool OffsetPreviewCenterOutfits { get; set; } = false;
			public bool AddonDuplicateFrameEnabled { get; set; } = false;
			public bool AddonRotateCloneDirectionEnabled { get; set; } = false;
			public bool AllowRelocatingDirection { get; set; } = false;
			public string DefaultLaunchSection { get; set; } = "Home";
			public string LastAssetExportFormat { get; set; } = "png";
			public string LastAssetExportDirectory { get; set; } = "";
			public string LastAssetImportDirectory { get; set; } = "";
			public bool LastThingExportSkipWest { get; set; }
			public bool ThingEditorShowAllDirections { get; set; }
			public bool ThingEditorShowTimeframe { get; set; }
			public bool ThingEditorAutoRotate { get; set; }
			public int ThingEditorRotateSpeedMs { get; set; } = 500;
			public SlicerStateModel Slicer { get; set; } = new();
		}

		public class SlicerStateModel
		{
			public bool SnapSelectionToGrid { get; set; } = true;
			public bool ContinuousDropIn { get; set; } = true;
			public string LastOpenDirectory { get; set; } = "";
			public string LastExportDirectory { get; set; } = "";
			public int ThingWidth { get; set; }
			public int ThingHeight { get; set; }
			public int ThingExactSize { get; set; } = 32;
			public bool AutomaticCropSize { get; set; } = true;
			public int ThingLayers { get; set; } = 1;
			public int ThingPatternX { get; set; } = 1;
			public int ThingPatternY { get; set; } = 1;
			public int ThingPatternZ { get; set; } = 1;
			public int ThingFrames { get; set; } = 1;
			public int OutfitDirections { get; set; } = 4;
			public int OutfitFrames { get; set; } = 3;
			public bool OutfitSeparateFrameGroups { get; set; }
			public int OutfitIdleFrames { get; set; } = 1;
			public int OutfitWalkingFrames { get; set; } = 2;
			public string ThingKind { get; set; } = "Item";
			public bool ReplaceExisting { get; set; }
		}

		public class AppStateTomlModel
		{
			public AssetsStateModel Assets { get; set; } = new AssetsStateModel();
			public List<RecentCombinationModel> RecentCombinations { get; set; } = new List<RecentCombinationModel>();
		}

		public class RecentCombinationModel
		{
			public string SpritePath { get; set; } = "";
			public string ThingsPath { get; set; } = "";
			public string LastUsed { get; set; } = "";

			// Sprite settings
			public bool SpriteGuessSettingsFromSignature { get; set; } = true;
			public bool SpritePreferOtfiSettings { get; set; }
			public bool SpriteUseTransparentPixels { get; set; } = true;
			public bool SpriteUseExtendedSpriteIds { get; set; } = true;

			// Things settings
			public bool ThingsGuessSettingsFromSignature { get; set; } = true;
			public bool ThingsPreferOtfiSettings { get; set; }
			public bool ThingsUseExtendedThingIds { get; set; } = true;
			public bool ThingsUseFrameAnimations { get; set; } = true;
			public bool ThingsUseFrameGroups { get; set; } = true;
		}

		public class AssetsStateModel
		{
			public double ColumnsWidthLeft { get; set; } = 0.25;
			public double ColumnsWidthCenter { get; set; } = 0.5;
			public double ColumnsWidthRight { get; set; } = 0.25;
			public List<PanelStateModel> Panels { get; set; } = new List<PanelStateModel>();
		}

		public class PanelStateModel
		{
			public string Type { get; set; } = ""; // "Sprite", "Things", or "Looktype"
			public string DockState { get; set; } = "Floating";
			public bool IsMinimized { get; set; }
			public double PositionX { get; set; }
			public double PositionY { get; set; }
			public double PanelWidth { get; set; }
			public double ContentHeight { get; set; }
			public string FilePath { get; set; } = "";
			public bool IsGridView { get; set; } = true;
			public int PageSize { get; set; } = 100;
			public int CurrentPage { get; set; } = 1;
			public bool GuessSettingsFromSignature { get; set; } = true;
			public bool PreferOtfiSettings { get; set; }

			// Sprite-specific
			public bool UseTransparentPixels { get; set; } = true;
			public bool UseExtendedSpriteIds { get; set; } = true;

			// Things-specific
			public bool UseExtendedThingIds { get; set; } = true;
			public bool UseFrameAnimations { get; set; } = true;
			public bool UseFrameGroups { get; set; } = true;
			public string LinkedSpriteFilePath { get; set; } = "";

			// Looktype-generator-specific
			public string SelectedLooktypeSpritePath { get; set; } = "";
			public string SelectedLooktypeThingsPath { get; set; } = "";

			// Floating editor/finder specific
			public uint ThingId { get; set; }
			public List<uint> ThingIds { get; set; } = new List<uint>();
			public string SourceFilePath { get; set; } = "";
			public string SelectedKind { get; set; } = "";
		}

		public static void LoadSettings()
		{
			try
			{
				if (File.Exists(SettingsPath))
				{
					string toml = File.ReadAllText(SettingsPath);
					var model = TomlSerializer.Deserialize<SettingsTomlModel>(toml);
					if (model != null)
					{
						_slicerState = model.Slicer ?? new SlicerStateModel();
						SettingsViewModel.SetSettings(
							model.DefaultPageSize,
							model.UseTransparentPixels,
							model.UseExtendedSpriteIds,
							model.ThingIdOffset,
							model.ClientVersion,
							model.PreloadGraphicalAssets,
							model.AssetDisplaySize,
							model.ItemAnimationDurationMs,
							model.OutfitAnimationDurationMs,
							model.EffectAnimationDurationMs,
							model.MissileAnimationDurationMs,
							model.ThingEditorGridColor,
							model.ThingEditorGridLineWidth,
							model.ThingEditorDragGridColor,
							model.ThingEditorDragGridLineWidth,
							model.ThingEditorDragHighlightColor,
							model.MaxRecentCombinations,
							model.UndoLimit,
							model.AllowUnknownSignatures,
							model.LooktypeMountAlignment,
							model.LooktypeMountedRiderOffsetX,
							model.LooktypeMountedRiderOffsetY,
							model.CompileLinkedPairTogether,
							model.CustomAccentColor,
							model.ShowInformationBoxes,
							model.DefaultSpritePanelWidth,
							model.DefaultSpritePanelHeight,
							model.DefaultThingsPanelWidth,
							model.DefaultThingsPanelHeight,
							model.SaveFloatingPanels,
							model.OffsetPreviewCenterOutfits,
							model.AddonDuplicateFrameEnabled,
							model.AddonRotateCloneDirectionEnabled,
							model.AllowRelocatingDirection,
							System.Enum.TryParse<SettingsViewModel.LaunchSection>(model.DefaultLaunchSection, true, out var section) ? section : SettingsViewModel.LaunchSection.Home,
							model.LastAssetExportFormat,
							model.LastAssetExportDirectory,
							model.LastAssetImportDirectory,
							model.LastThingExportSkipWest,
							model.ThingEditorShowAllDirections,
							model.ThingEditorShowTimeframe,
							model.ThingEditorAutoRotate,
							model.ThingEditorRotateSpeedMs);
					}
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Failed to load settings.toml: {ex.Message}");
			}
		}

		public static void SaveSettings()
		{
			if (_isRestoring) return;
			try
			{
				var model = new SettingsTomlModel
				{
					DefaultPageSize = SettingsViewModel.DefaultPageSize,
					UseTransparentPixels = SettingsViewModel.UseTransparentPixels,
					UseExtendedSpriteIds = SettingsViewModel.UseExtendedSpriteIds,
					PreloadGraphicalAssets = SettingsViewModel.PreloadGraphicalAssets,
					AssetDisplaySize = SettingsViewModel.AssetDisplaySize,
					ThingIdOffset = SettingsViewModel.ThingIdOffset,
					ClientVersion = SettingsViewModel.ClientVersion,
					ItemAnimationDurationMs = SettingsViewModel.ItemAnimationDurationMs,
					OutfitAnimationDurationMs = SettingsViewModel.OutfitAnimationDurationMs,
					EffectAnimationDurationMs = SettingsViewModel.EffectAnimationDurationMs,
					MissileAnimationDurationMs = SettingsViewModel.MissileAnimationDurationMs,
					LooktypeMountAlignment = SettingsViewModel.LooktypeMountAlignment.ToString(),
					LooktypeMountedRiderOffsetX = SettingsViewModel.LooktypeMountedRiderOffsetX,
					LooktypeMountedRiderOffsetY = SettingsViewModel.LooktypeMountedRiderOffsetY,
					ThingEditorGridColor = SettingsViewModel.ThingEditorGridColor,
					ThingEditorGridLineWidth = SettingsViewModel.ThingEditorGridLineWidth,
					ThingEditorDragGridColor = SettingsViewModel.ThingEditorDragGridColor,
					ThingEditorDragGridLineWidth = SettingsViewModel.ThingEditorDragGridLineWidth,
					ThingEditorDragHighlightColor = SettingsViewModel.ThingEditorDragHighlightColor,
					MaxRecentCombinations = SettingsViewModel.MaxRecentCombinations,
					UndoLimit = SettingsViewModel.UndoLimit,
					AllowUnknownSignatures = SettingsViewModel.AllowUnknownSignatures,
					CompileLinkedPairTogether = SettingsViewModel.CompileLinkedPairTogether,
					ShowInformationBoxes = SettingsViewModel.ShowInformationBoxes,
					CustomAccentColor = SettingsViewModel.CustomAccentColor,
					DefaultSpritePanelWidth = SettingsViewModel.DefaultSpritePanelWidth,
					DefaultSpritePanelHeight = SettingsViewModel.DefaultSpritePanelHeight,
					DefaultThingsPanelWidth = SettingsViewModel.DefaultThingsPanelWidth,
					DefaultThingsPanelHeight = SettingsViewModel.DefaultThingsPanelHeight,
					SaveFloatingPanels = SettingsViewModel.SaveFloatingPanels,
					OffsetPreviewCenterOutfits = SettingsViewModel.OffsetPreviewCenterOutfits,
					AddonDuplicateFrameEnabled = SettingsViewModel.AddonDuplicateFrameEnabled,
					AddonRotateCloneDirectionEnabled = SettingsViewModel.AddonRotateCloneDirectionEnabled,
					AllowRelocatingDirection = SettingsViewModel.AllowRelocatingDirection,
					DefaultLaunchSection = SettingsViewModel.DefaultLaunchSection.ToString(),
					LastAssetExportFormat = SettingsViewModel.LastAssetExportFormat,
					LastAssetExportDirectory = SettingsViewModel.LastAssetExportDirectory,
					LastAssetImportDirectory = SettingsViewModel.LastAssetImportDirectory,
					LastThingExportSkipWest = SettingsViewModel.LastThingExportSkipWest,
					ThingEditorShowAllDirections = SettingsViewModel.ThingEditorShowAllDirections,
					ThingEditorShowTimeframe = SettingsViewModel.ThingEditorShowTimeframe,
					ThingEditorAutoRotate = SettingsViewModel.ThingEditorAutoRotate,
					ThingEditorRotateSpeedMs = SettingsViewModel.ThingEditorRotateSpeedMs,
					Slicer = _slicerState
				};
				string toml = TomlSerializer.Serialize(model);
				File.WriteAllText(SettingsPath, toml);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Failed to save settings.toml: {ex.Message}");
			}
		}

		public static SlicerStateModel GetSlicerState() => new()
		{
			SnapSelectionToGrid = _slicerState.SnapSelectionToGrid,
			LastOpenDirectory = _slicerState.LastOpenDirectory,
			LastExportDirectory = _slicerState.LastExportDirectory,
			ThingWidth = _slicerState.ThingWidth,
			ThingHeight = _slicerState.ThingHeight,
			ThingExactSize = _slicerState.ThingExactSize,
			AutomaticCropSize = _slicerState.AutomaticCropSize,
			ThingLayers = _slicerState.ThingLayers,
			ThingPatternX = _slicerState.ThingPatternX,
			ThingPatternY = _slicerState.ThingPatternY,
			ThingPatternZ = _slicerState.ThingPatternZ,
			ThingFrames = _slicerState.ThingFrames,
			OutfitDirections = _slicerState.OutfitDirections,
			OutfitFrames = _slicerState.OutfitFrames,
			OutfitSeparateFrameGroups = _slicerState.OutfitSeparateFrameGroups,
			OutfitIdleFrames = _slicerState.OutfitIdleFrames,
			OutfitWalkingFrames = _slicerState.OutfitWalkingFrames,
			ThingKind = _slicerState.ThingKind,
			ReplaceExisting = _slicerState.ReplaceExisting
		};

		public static void SaveSlicerState(SlicerStateModel state)
		{
			_slicerState = state;
			SaveSettings();
		}

		public static void SaveAppState(AssetsViewModel assetsVm)
		{
			if (_isRestoring) return;
			try
			{
				var model = new AppStateTomlModel();

				if (File.Exists(AppStatePath))
				{
					try
					{
						string existingToml = File.ReadAllText(AppStatePath);
						var existing = TomlSerializer.Deserialize<AppStateTomlModel>(existingToml);
						if (existing?.RecentCombinations != null)
						{
							model.RecentCombinations = existing.RecentCombinations;
						}
					}
					catch
					{
						// Ignore
					}
				}

				foreach (var panel in assetsVm.ActivePanels)
				{
					if (panel is FloatingThingFinderViewModel or FloatingReplacerViewModel) continue;
					if (panel.DockState == "Floating") continue;

					var state = new PanelStateModel
					{
						DockState = panel.DockState,
						IsMinimized = panel.IsMinimized,
						PositionX = panel.PositionX,
						PositionY = panel.PositionY,
						PanelWidth = panel.PanelWidth,
						ContentHeight = panel.ContentHeight
					};

					if (panel is FloatingSpriteLoaderViewModel spritePanel)
					{
						state.Type = "Sprite";
						state.FilePath = spritePanel.FilePath == "No archive loaded" ? "" : spritePanel.FilePath;
						state.IsGridView = spritePanel.IsGridView;
						state.PageSize = spritePanel.PageSize;
						state.CurrentPage = spritePanel.CurrentPage;
						state.UseTransparentPixels = spritePanel.UseTransparentPixels;
						state.UseExtendedSpriteIds = spritePanel.UseExtendedSpriteIds;
						state.GuessSettingsFromSignature = spritePanel.GuessSettingsFromSignature;
						state.PreferOtfiSettings = spritePanel.PreferOtfiSettings;
					}
					else if (panel is FloatingThingsLoaderViewModel thingsPanel)
					{
						state.Type = "Things";
						state.FilePath = thingsPanel.FilePath == "No things loaded" ? "" : thingsPanel.FilePath;
						state.IsGridView = thingsPanel.IsGridView;
						state.PageSize = thingsPanel.PageSize;
						state.CurrentPage = thingsPanel.CurrentPage;
						state.UseExtendedThingIds = thingsPanel.UseExtendedThingIds;
						state.UseFrameAnimations = thingsPanel.UseFrameAnimations;
						state.UseFrameGroups = thingsPanel.UseFrameGroups;
						state.LinkedSpriteFilePath = thingsPanel.LinkedSpritePanel?.FilePath ?? "";
						state.GuessSettingsFromSignature = thingsPanel.GuessSettingsFromSignature;
						state.PreferOtfiSettings = thingsPanel.PreferOtfiSettings;
					}
					else if (panel is FloatingLooktypeGeneratorViewModel looktypePanel)
					{
						state.Type = "Looktype";
						state.SelectedLooktypeSpritePath = looktypePanel.SelectedSpritePath;
						state.SelectedLooktypeThingsPath = looktypePanel.SelectedThingsPath;
					}
					else if (panel is SpritesheetSlicerViewModel)
					{
						state.Type = "Slicer";
					}

					model.Assets.Panels.Add(state);
				}

				string toml = TomlSerializer.Serialize(model);
				File.WriteAllText(AppStatePath, toml);

				SaveFloatingAppState(assetsVm);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Failed to save app_state.toml: {ex.Message}");
			}
		}

		public static async System.Threading.Tasks.Task LoadAppStateAsync(AssetsViewModel assetsVm, SpriteRenderer spriteRenderer)
		{
			_isRestoring = true;
			try
			{
				if (!File.Exists(AppStatePath)) return;

				string toml = File.ReadAllText(AppStatePath);
				var model = TomlSerializer.Deserialize<AppStateTomlModel>(toml);
				if (model == null || model.Assets == null || model.Assets.Panels == null) return;

				assetsVm.ClearAllPanels();

				var spritePanels = new List<(PanelStateModel state, FloatingSpriteLoaderViewModel panel)>();
				var thingsPanels = new List<(PanelStateModel state, FloatingThingsLoaderViewModel panel)>();
				var looktypeStates = new List<PanelStateModel>();

				foreach (var panelState in model.Assets.Panels)
				{
					// Archive and slicer panels restore only when docked.
					if (panelState.DockState == "Floating" || string.IsNullOrEmpty(panelState.DockState)) continue;

					if (panelState.Type == "Sprite")
					{
						var panel = new FloatingSpriteLoaderViewModel(spriteRenderer)
						{
							DockState = panelState.DockState,
							IsMinimized = panelState.IsMinimized,
							PositionX = panelState.PositionX,
							PositionY = panelState.PositionY,
							PanelWidth = panelState.PanelWidth,
							ContentHeight = panelState.ContentHeight,
							IsGridView = panelState.IsGridView,
							PageSize = panelState.PageSize,
							UseTransparentPixels = panelState.UseTransparentPixels,
							UseExtendedSpriteIds = panelState.UseExtendedSpriteIds,
							IsDefaultPosition = false,
							GuessSettingsFromSignature = panelState.GuessSettingsFromSignature,
							PreferOtfiSettings = panelState.PreferOtfiSettings
						};

						assetsVm.RestorePanel(panel);
						spritePanels.Add((panelState, panel));
					}
					else if (panelState.Type == "Things")
					{
						var panel = new FloatingThingsLoaderViewModel(assetsVm)
						{
							DockState = panelState.DockState,
							IsMinimized = panelState.IsMinimized,
							PositionX = panelState.PositionX,
							PositionY = panelState.PositionY,
							PanelWidth = panelState.PanelWidth,
							ContentHeight = panelState.ContentHeight,
							IsGridView = panelState.IsGridView,
							PageSize = panelState.PageSize,
							UseExtendedThingIds = panelState.UseExtendedThingIds,
							UseFrameAnimations = panelState.UseFrameAnimations,
							UseFrameGroups = panelState.UseFrameGroups,
							IsDefaultPosition = false,
							GuessSettingsFromSignature = panelState.GuessSettingsFromSignature,
							PreferOtfiSettings = panelState.PreferOtfiSettings
						};

						assetsVm.RestorePanel(panel);
						thingsPanels.Add((panelState, panel));
					}
					else if (panelState.Type == "Looktype")
					{
						looktypeStates.Add(panelState);
					}
					else if (panelState.Type == "Slicer")
					{
						assetsVm.RestorePanel(new SpritesheetSlicerViewModel(assetsVm)
						{
							DockState = panelState.DockState,
							IsMinimized = panelState.IsMinimized,
							PositionX = panelState.PositionX,
							PositionY = panelState.PositionY,
							PanelWidth = panelState.PanelWidth <= 0
								? SpritesheetSlicerViewModel.DefaultPanelWidth
								: panelState.PanelWidth,
							ContentHeight = panelState.ContentHeight <= 0
								? SpritesheetSlicerViewModel.DefaultContentHeight
								: panelState.ContentHeight,
							IsDefaultPosition = false,
						});
					}
				}

				foreach (var (panelState, panel) in spritePanels)
				{
					if (!string.IsNullOrEmpty(panelState.FilePath) && File.Exists(panelState.FilePath))
					{
						try
						{
							await panel.LoadArchiveAsync(panelState.FilePath).ConfigureAwait(true);
							panel.CurrentPage = panelState.CurrentPage;
						}
						catch (Exception ex)
						{
							Debug.WriteLine($"Failed to load spr/assets from state: {ex.Message}");
						}
					}
				}

				foreach (var (panelState, panel) in thingsPanels)
				{
					assetsVm.RestoreThingsLink(panel, panelState.LinkedSpriteFilePath);

					if (!string.IsNullOrEmpty(panelState.FilePath) && File.Exists(panelState.FilePath))
					{
						try
						{
							await panel.LoadArchiveAsync(panelState.FilePath, useLastLoadedSprite: false).ConfigureAwait(true);
							panel.CurrentPage = panelState.CurrentPage;
						}
						catch (Exception ex)
						{
							Debug.WriteLine($"Failed to load dat/things from state: {ex.Message}");
						}
					}
				}

				foreach (var panelState in looktypeStates)
				{
					var panel = new FloatingLooktypeGeneratorViewModel(assetsVm)
					{
						DockState = panelState.DockState,
						IsMinimized = panelState.IsMinimized,
						PositionX = panelState.PositionX,
						PositionY = panelState.PositionY,
						PanelWidth = panelState.PanelWidth <= 0
							? FloatingLooktypeGeneratorViewModel.DefaultPanelWidth
							: panelState.PanelWidth,
						ContentHeight = panelState.ContentHeight <= 0
							? FloatingLooktypeGeneratorViewModel.DefaultContentHeight
							: panelState.ContentHeight,
						IsDefaultPosition = false,
					};
					assetsVm.RestorePanel(panel);
					panel.RefreshArchivePairs(panelState.SelectedLooktypeSpritePath, panelState.SelectedLooktypeThingsPath);
				}

				if (SettingsViewModel.SaveFloatingPanels)
				{
					await LoadFloatingAppStateAsync(assetsVm, spriteRenderer).ConfigureAwait(true);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Failed to load app_state.toml: {ex.Message}");
			}
			finally
			{
				_isRestoring = false;
			}
		}

		public static void AddRecentCombination(
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
			try
			{
				var model = new AppStateTomlModel();
				if (File.Exists(AppStatePath))
				{
					try
					{
						string toml = File.ReadAllText(AppStatePath);
						var existing = TomlSerializer.Deserialize<AppStateTomlModel>(toml);
						if (existing != null)
							model = existing;
					}
					catch
					{
						// Ignore
					}
				}

				if (model.RecentCombinations == null)
					model.RecentCombinations = new List<RecentCombinationModel>();

				// Normalize paths for comparison
				string normSprite = string.IsNullOrEmpty(spritePath) ? "" : Path.GetFullPath(spritePath);
				string normThings = string.IsNullOrEmpty(thingsPath) ? "" : Path.GetFullPath(thingsPath);

				// Remove duplicates (case-insensitive comparison)
				model.RecentCombinations.RemoveAll(rc =>
				{
					string s = string.IsNullOrEmpty(rc.SpritePath) ? "" : Path.GetFullPath(rc.SpritePath);
					string t = string.IsNullOrEmpty(rc.ThingsPath) ? "" : Path.GetFullPath(rc.ThingsPath);
					return string.Equals(s, normSprite, StringComparison.OrdinalIgnoreCase) &&
						   string.Equals(t, normThings, StringComparison.OrdinalIgnoreCase);
				});

				// Insert at beginning
				model.RecentCombinations.Insert(0, new RecentCombinationModel
				{
					SpritePath = spritePath ?? "",
					ThingsPath = thingsPath ?? "",
					LastUsed = DateTime.Now.ToString("o"),
					SpriteGuessSettingsFromSignature = spriteGuess,
					SpritePreferOtfiSettings = spritePreferOtfi,
					SpriteUseTransparentPixels = spriteTransparent,
					SpriteUseExtendedSpriteIds = spriteExtended,
					ThingsGuessSettingsFromSignature = thingsGuess,
					ThingsPreferOtfiSettings = thingsPreferOtfi,
					ThingsUseExtendedThingIds = thingsExtended,
					ThingsUseFrameAnimations = thingsAnimations,
					ThingsUseFrameGroups = thingsGroups
				});

				// Keep configured entries count
				int maxCombinations = SettingsViewModel.MaxRecentCombinations;
				if (maxCombinations < 4 || maxCombinations > 20)
				{
					maxCombinations = 10;
				}

				if (model.RecentCombinations.Count > maxCombinations)
				{
					model.RecentCombinations.RemoveRange(maxCombinations, model.RecentCombinations.Count - maxCombinations);
				}

				string serialized = TomlSerializer.Serialize(model);
				File.WriteAllText(AppStatePath, serialized);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Failed to save recent combination: {ex.Message}");
			}
		}

		public static void RemoveRecentCombination(string spritePath, string thingsPath)
		{
			try
			{
				if (!File.Exists(AppStatePath))
					return;

				string toml = File.ReadAllText(AppStatePath);
				var model = TomlSerializer.Deserialize<AppStateTomlModel>(toml);
				if (model?.RecentCombinations == null)
					return;

				string normSprite = string.IsNullOrEmpty(spritePath) ? "" : Path.GetFullPath(spritePath);
				string normThings = string.IsNullOrEmpty(thingsPath) ? "" : Path.GetFullPath(thingsPath);

				model.RecentCombinations.RemoveAll(rc =>
				{
					string s = string.IsNullOrEmpty(rc.SpritePath) ? "" : Path.GetFullPath(rc.SpritePath);
					string t = string.IsNullOrEmpty(rc.ThingsPath) ? "" : Path.GetFullPath(rc.ThingsPath);
					return string.Equals(s, normSprite, StringComparison.OrdinalIgnoreCase) &&
						   string.Equals(t, normThings, StringComparison.OrdinalIgnoreCase);
				});

				string serialized = TomlSerializer.Serialize(model);
				File.WriteAllText(AppStatePath, serialized);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Failed to remove recent combination: {ex.Message}");
			}
		}

		public static List<RecentCombinationModel> GetRecentCombinations()
		{
			try
			{
				if (File.Exists(AppStatePath))
				{
					string toml = File.ReadAllText(AppStatePath);
					var model = TomlSerializer.Deserialize<AppStateTomlModel>(toml);
					if (model != null && model.RecentCombinations != null)
					{
						return model.RecentCombinations;
					}
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Failed to load recent combinations: {ex.Message}");
			}
			return new List<RecentCombinationModel>();
		}

		public class PaintLayerModel
		{
			public string Name { get; set; } = "";
			public bool IsVisible { get; set; } = true;
			public double Opacity { get; set; } = 1.0;
			public string Pixels { get; set; } = "";
		}

		public class PaintStateModel
		{
			public string SpriteFilePath { get; set; } = "";
			public uint SpriteId { get; set; }
			public List<PaintLayerModel> Layers { get; set; } = new List<PaintLayerModel>();
			public int ActiveLayerIndex { get; set; }
			public string ActiveTool { get; set; } = "Brush";
			public int BrushSize { get; set; } = 1;
			public string BrushShape { get; set; } = "Square";
			public double ZoomLevel { get; set; } = 12.0;
			public int ColorR { get; set; } = 255;
			public int ColorG { get; set; } = 255;
			public int ColorB { get; set; } = 255;
			public bool CopyOnAxisX { get; set; }
			public bool CopyOnAxisY { get; set; }
			public double FillThreshold { get; set; } = 10.0;
			public bool CheckDiagonals { get; set; } = true;
			public bool ShowFillPreview { get; set; } = true;
			public string SelectedPaletteName { get; set; } = "";
			public int CanvasWidth { get; set; } = 32;
			public int CanvasHeight { get; set; } = 32;
			public string GridColor { get; set; } = "#FF000000";
		}

		private static void SaveFloatingAppState(AssetsViewModel assetsVm)
		{
			try
			{
				if (!SettingsViewModel.SaveFloatingPanels)
				{
					if (File.Exists(FloatingSaveDataPath))
					{
						File.Delete(FloatingSaveDataPath);
					}
					return;
				}

				var model = new FloatingStateTomlModel();

				foreach (var panel in assetsVm.ActivePanels)
				{
					if (panel.DockState != "Floating") continue;

					var state = new PanelStateModel
					{
						DockState = panel.DockState,
						IsMinimized = panel.IsMinimized,
						PositionX = panel.PositionX,
						PositionY = panel.PositionY,
						PanelWidth = panel.PanelWidth,
						ContentHeight = panel.ContentHeight
					};

					if (panel is FloatingSpriteLoaderViewModel spritePanel)
					{
						state.Type = "Sprite";
						state.FilePath = spritePanel.FilePath == "No archive loaded" ? "" : spritePanel.FilePath;
						state.IsGridView = spritePanel.IsGridView;
						state.PageSize = spritePanel.PageSize;
						state.CurrentPage = spritePanel.CurrentPage;
						state.UseTransparentPixels = spritePanel.UseTransparentPixels;
						state.UseExtendedSpriteIds = spritePanel.UseExtendedSpriteIds;
						state.GuessSettingsFromSignature = spritePanel.GuessSettingsFromSignature;
						state.PreferOtfiSettings = spritePanel.PreferOtfiSettings;
					}
					else if (panel is FloatingThingsLoaderViewModel thingsPanel)
					{
						state.Type = "Things";
						state.FilePath = thingsPanel.FilePath == "No things loaded" ? "" : thingsPanel.FilePath;
						state.IsGridView = thingsPanel.IsGridView;
						state.PageSize = thingsPanel.PageSize;
						state.CurrentPage = thingsPanel.CurrentPage;
						state.UseExtendedThingIds = thingsPanel.UseExtendedThingIds;
						state.UseFrameAnimations = thingsPanel.UseFrameAnimations;
						state.UseFrameGroups = thingsPanel.UseFrameGroups;
						state.LinkedSpriteFilePath = thingsPanel.LinkedSpritePanel?.FilePath ?? "";
						state.GuessSettingsFromSignature = thingsPanel.GuessSettingsFromSignature;
						state.PreferOtfiSettings = thingsPanel.PreferOtfiSettings;
					}
					else if (panel is FloatingLooktypeGeneratorViewModel looktypePanel)
					{
						state.Type = "Looktype";
						state.SelectedLooktypeSpritePath = looktypePanel.SelectedSpritePath;
						state.SelectedLooktypeThingsPath = looktypePanel.SelectedThingsPath;
					}
					else if (panel is SpritesheetSlicerViewModel)
					{
						state.Type = "Slicer";
					}
					else if (panel is FloatingReplacerViewModel)
					{
						state.Type = "Replacer";
					}
					else if (panel is FloatingWebExportViewModel)
					{
						state.Type = "WebExport";
					}
					else if (panel is FloatingCompileViewModel)
					{
						state.Type = "Compile";
					}
					else if (panel is FloatingThingFinderViewModel finderPanel)
					{
						state.Type = "ThingFinder";
						state.SourceFilePath = finderPanel.SourcePanel?.FilePath ?? "";
						state.SelectedKind = finderPanel.SelectedKind.ToString();
					}
					else if (panel is FloatingThingEditorViewModel editorPanel)
					{
						state.Type = "ThingEditor";
						state.SourceFilePath = editorPanel.SourcePanel?.FilePath ?? "";
						state.ThingId = editorPanel.ThingId;
					}
					else if (panel is FloatingMultiThingEditorViewModel multiEditorPanel)
					{
						state.Type = "MultiThingEditor";
						state.SourceFilePath = multiEditorPanel.SourcePanel?.FilePath ?? "";
						if (multiEditorPanel.Entries != null)
						{
							foreach (var entry in multiEditorPanel.Entries)
							{
								state.ThingIds.Add(entry.Id);
							}
						}
					}

					model.Panels.Add(state);
				}

				string toml = TomlSerializer.Serialize(model);
				File.WriteAllText(FloatingSaveDataPath, toml);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Failed to save floating_save_data.toml: {ex.Message}");
			}
		}

		private static async System.Threading.Tasks.Task LoadFloatingAppStateAsync(AssetsViewModel assetsVm, SpriteRenderer spriteRenderer)
		{
			try
			{
				if (!File.Exists(FloatingSaveDataPath)) return;

				string toml = File.ReadAllText(FloatingSaveDataPath);
				var model = TomlSerializer.Deserialize<FloatingStateTomlModel>(toml);
				if (model == null || model.Panels == null) return;

				var spritePanels = new List<(PanelStateModel state, FloatingSpriteLoaderViewModel panel)>();
				var thingsPanels = new List<(PanelStateModel state, FloatingThingsLoaderViewModel panel)>();
				var otherPanels = new List<PanelStateModel>();

				foreach (var panelState in model.Panels)
				{
					if (panelState.Type == "Sprite")
					{
						var panel = new FloatingSpriteLoaderViewModel(spriteRenderer)
						{
							DockState = panelState.DockState,
							IsMinimized = panelState.IsMinimized,
							PositionX = panelState.PositionX,
							PositionY = panelState.PositionY,
							PanelWidth = panelState.PanelWidth,
							ContentHeight = panelState.ContentHeight,
							IsGridView = panelState.IsGridView,
							PageSize = panelState.PageSize,
							UseTransparentPixels = panelState.UseTransparentPixels,
							UseExtendedSpriteIds = panelState.UseExtendedSpriteIds,
							IsDefaultPosition = false,
							GuessSettingsFromSignature = panelState.GuessSettingsFromSignature,
							PreferOtfiSettings = panelState.PreferOtfiSettings
						};

						assetsVm.RestorePanel(panel);
						spritePanels.Add((panelState, panel));
					}
					else if (panelState.Type == "Things")
					{
						var panel = new FloatingThingsLoaderViewModel(assetsVm)
						{
							DockState = panelState.DockState,
							IsMinimized = panelState.IsMinimized,
							PositionX = panelState.PositionX,
							PositionY = panelState.PositionY,
							PanelWidth = panelState.PanelWidth,
							ContentHeight = panelState.ContentHeight,
							IsGridView = panelState.IsGridView,
							PageSize = panelState.PageSize,
							UseExtendedThingIds = panelState.UseExtendedThingIds,
							UseFrameAnimations = panelState.UseFrameAnimations,
							UseFrameGroups = panelState.UseFrameGroups,
							IsDefaultPosition = false,
							GuessSettingsFromSignature = panelState.GuessSettingsFromSignature,
							PreferOtfiSettings = panelState.PreferOtfiSettings
						};

						assetsVm.RestorePanel(panel);
						thingsPanels.Add((panelState, panel));
					}
					else
					{
						otherPanels.Add(panelState);
					}
				}

				foreach (var (panelState, panel) in spritePanels)
				{
					if (!string.IsNullOrEmpty(panelState.FilePath) && File.Exists(panelState.FilePath))
					{
						try
						{
							await panel.LoadArchiveAsync(panelState.FilePath).ConfigureAwait(true);
							panel.CurrentPage = panelState.CurrentPage;
						}
						catch (Exception ex)
						{
							Debug.WriteLine($"Failed to load spr/assets from state: {ex.Message}");
						}
					}
				}

				foreach (var (panelState, panel) in thingsPanels)
				{
					assetsVm.RestoreThingsLink(panel, panelState.LinkedSpriteFilePath);

					if (!string.IsNullOrEmpty(panelState.FilePath) && File.Exists(panelState.FilePath))
					{
						try
						{
							await panel.LoadArchiveAsync(panelState.FilePath, useLastLoadedSprite: false).ConfigureAwait(true);
							panel.CurrentPage = panelState.CurrentPage;
						}
						catch (Exception ex)
						{
							Debug.WriteLine($"Failed to load dat/things from state: {ex.Message}");
						}
					}
				}

				// Restore other panels now that source loaders are loaded and indexed
				foreach (var panelState in otherPanels)
				{
					if (panelState.Type == "Looktype")
					{
						var panel = new FloatingLooktypeGeneratorViewModel(assetsVm)
						{
							DockState = panelState.DockState,
							IsMinimized = panelState.IsMinimized,
							PositionX = panelState.PositionX,
							PositionY = panelState.PositionY,
							PanelWidth = panelState.PanelWidth <= 0
								? FloatingLooktypeGeneratorViewModel.DefaultPanelWidth
								: panelState.PanelWidth,
							ContentHeight = panelState.ContentHeight <= 0
								? FloatingLooktypeGeneratorViewModel.DefaultContentHeight
								: panelState.ContentHeight,
							IsDefaultPosition = false,
						};
						assetsVm.RestorePanel(panel);
						panel.RefreshArchivePairs(panelState.SelectedLooktypeSpritePath, panelState.SelectedLooktypeThingsPath);
					}
					else if (panelState.Type == "Slicer")
					{
						var panel = new SpritesheetSlicerViewModel(assetsVm)
						{
							DockState = panelState.DockState,
							IsMinimized = panelState.IsMinimized,
							PositionX = panelState.PositionX,
							PositionY = panelState.PositionY,
							PanelWidth = panelState.PanelWidth <= 0
								? SpritesheetSlicerViewModel.DefaultPanelWidth
								: panelState.PanelWidth,
							ContentHeight = panelState.ContentHeight <= 0
								? SpritesheetSlicerViewModel.DefaultContentHeight
								: panelState.ContentHeight,
							IsDefaultPosition = false,
						};
						assetsVm.RestorePanel(panel);
					}
					else if (panelState.Type == "Replacer")
					{
						var panel = new FloatingReplacerViewModel(assetsVm)
						{
							DockState = panelState.DockState,
							IsMinimized = panelState.IsMinimized,
							PositionX = panelState.PositionX,
							PositionY = panelState.PositionY,
							PanelWidth = panelState.PanelWidth <= 0
								? FloatingReplacerViewModel.DefaultPanelWidth
								: panelState.PanelWidth,
							ContentHeight = panelState.ContentHeight <= 0
								? FloatingReplacerViewModel.DefaultContentHeight
								: panelState.ContentHeight,
							IsDefaultPosition = false,
						};
						assetsVm.RestorePanel(panel);
						panel.RefreshArchivePairs();
					}
					else if (panelState.Type == "WebExport")
					{
						var panel = new FloatingWebExportViewModel(assetsVm)
						{
							DockState = panelState.DockState,
							IsMinimized = panelState.IsMinimized,
							PositionX = panelState.PositionX,
							PositionY = panelState.PositionY,
							PanelWidth = panelState.PanelWidth <= 0
								? 550
								: panelState.PanelWidth,
							ContentHeight = panelState.ContentHeight <= 0
								? 600
								: panelState.ContentHeight,
							IsDefaultPosition = false,
						};
						assetsVm.RestorePanel(panel);
						panel.RefreshArchivePairs();
					}
					else if (panelState.Type == "Compile")
					{
						var panel = new FloatingCompileViewModel(assetsVm)
						{
							DockState = panelState.DockState,
							IsMinimized = panelState.IsMinimized,
							PositionX = panelState.PositionX,
							PositionY = panelState.PositionY,
							PanelWidth = panelState.PanelWidth <= 0
								? FloatingCompileViewModel.DefaultPanelWidth
								: panelState.PanelWidth,
							ContentHeight = panelState.ContentHeight <= 0
								? FloatingCompileViewModel.DefaultContentHeight
								: panelState.ContentHeight,
							IsDefaultPosition = false,
						};
						assetsVm.RestorePanel(panel);
						panel.RefreshArchivePairs();
					}
					else if (panelState.Type == "ThingFinder")
					{
						var sourcePanel = assetsVm.ActivePanels
							.OfType<FloatingThingsLoaderViewModel>()
							.FirstOrDefault(p => p.FilePath == panelState.SourceFilePath);
						if (sourcePanel != null)
						{
							var panel = new FloatingThingFinderViewModel(assetsVm, sourcePanel)
							{
								DockState = panelState.DockState,
								IsMinimized = panelState.IsMinimized,
								PositionX = panelState.PositionX,
								PositionY = panelState.PositionY,
								PanelWidth = panelState.PanelWidth,
								ContentHeight = panelState.ContentHeight,
								IsDefaultPosition = false,
							};
							if (Enum.TryParse<NyxAssets.Things.ThingKind>(panelState.SelectedKind, out var kind))
							{
								panel.SelectedKind = kind;
							}
							assetsVm.RestorePanel(panel);
						}
					}
					else if (panelState.Type == "ThingEditor")
					{
						var sourcePanel = assetsVm.ActivePanels
							.OfType<FloatingThingsLoaderViewModel>()
							.FirstOrDefault(p => p.FilePath == panelState.SourceFilePath);
						if (sourcePanel != null)
						{
							var thing = sourcePanel.GetThingType(panelState.ThingId);
							if (thing != null)
							{
								var panel = new FloatingThingEditorViewModel(sourcePanel, thing)
								{
									DockState = panelState.DockState,
									IsMinimized = panelState.IsMinimized,
									PositionX = panelState.PositionX,
									PositionY = panelState.PositionY,
									PanelWidth = panelState.PanelWidth,
									ContentHeight = panelState.ContentHeight,
									IsDefaultPosition = false,
								};
								assetsVm.RestorePanel(panel);
							}
						}
					}
					else if (panelState.Type == "MultiThingEditor")
					{
						var sourcePanel = assetsVm.ActivePanels
							.OfType<FloatingThingsLoaderViewModel>()
							.FirstOrDefault(p => p.FilePath == panelState.SourceFilePath);
						if (sourcePanel != null)
						{
							var thingsList = new List<NyxAssets.Things.ThingType>();
							foreach (var id in panelState.ThingIds)
							{
								var t = sourcePanel.GetThingType(id);
								if (t != null) thingsList.Add(t);
							}
							if (thingsList.Count >= 2)
							{
								var panel = new FloatingMultiThingEditorViewModel(sourcePanel, thingsList)
								{
									DockState = panelState.DockState,
									IsMinimized = panelState.IsMinimized,
									PositionX = panelState.PositionX,
									PositionY = panelState.PositionY,
									PanelWidth = panelState.PanelWidth,
									ContentHeight = panelState.ContentHeight,
									IsDefaultPosition = false,
								};
								assetsVm.RestorePanel(panel);
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Failed to load floating_save_data.toml: {ex.Message}");
			}
		}

		private static readonly string PaintStatePath = Path.Combine(AppContext.BaseDirectory, "paint_state.toml");

		public static void SavePaintState(NyxAssetsEditor.ViewModels.Pages.PaintViewModel vm)
		{
			if (_isRestoring) return;
			try
			{
				if (vm.Sprite == null) return;
				string filePath = vm.Panel?.FilePath ?? "";
				if (filePath == "No archive loaded") filePath = "";

				var model = new PaintStateModel
				{
					SpriteFilePath = filePath,
					SpriteId = vm.Sprite.Id,
					ActiveLayerIndex = vm.ActiveLayer != null ? vm.Layers.IndexOf(vm.ActiveLayer) : 0,
					ActiveTool = vm.ActiveTool.ToString(),
					BrushSize = vm.BrushSize,
					BrushShape = vm.BrushShape.ToString(),
					ZoomLevel = vm.ZoomLevel,
					ColorR = vm.ActiveColor.R,
					ColorG = vm.ActiveColor.G,
					ColorB = vm.ActiveColor.B,
					CopyOnAxisX = vm.CopyOnAxisX,
					CopyOnAxisY = vm.CopyOnAxisY,
					FillThreshold = vm.FillThreshold,
					CheckDiagonals = vm.CheckDiagonals,
					ShowFillPreview = vm.ShowFillPreview,
					SelectedPaletteName = vm.SelectedPalette?.Name ?? "",
					CanvasWidth = vm.CanvasWidth,
					CanvasHeight = vm.CanvasHeight,
					GridColor = vm.GridColor.ToString()
				};

				foreach (var layer in vm.Layers)
				{
					model.Layers.Add(new PaintLayerModel
					{
						Name = layer.Name,
						IsVisible = layer.IsVisible,
						Opacity = layer.Opacity,
						Pixels = Convert.ToBase64String(layer.Pixels)
					});
				}

				string toml = TomlSerializer.Serialize(model);
				File.WriteAllText(PaintStatePath, toml);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Failed to save paint state: {ex.Message}");
			}
		}

		public static PaintStateModel? LoadPaintState()
		{
			try
			{
				if (!File.Exists(PaintStatePath)) return null;
				string toml = File.ReadAllText(PaintStatePath);
				return TomlSerializer.Deserialize<PaintStateModel>(toml);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Failed to load paint state: {ex.Message}");
				return null;
			}
		}
	}
}
