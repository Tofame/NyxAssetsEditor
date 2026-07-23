using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using Tomlyn;
using NyxAssetsEditor.Services.Rendering;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;
using NyxAssetsEditor.ViewModels.Core;
using NyxAssetsEditor.ViewModels.Pages;

namespace NyxAssetsEditor.Services.Persistence
{
	public static class PersistenceService
	{
		private static readonly string SettingsPath = Path.Combine(AppContext.BaseDirectory, "settings.toml");
		private static readonly string AppStatePath = Path.Combine(AppContext.BaseDirectory, "app_state.toml");

		private static bool _isRestoring;

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
							model.LooktypeMountedRiderOffsetY);
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
					AllowUnknownSignatures = SettingsViewModel.AllowUnknownSignatures
				};
				string toml = TomlSerializer.Serialize(model);
				File.WriteAllText(SettingsPath, toml);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Failed to save settings.toml: {ex.Message}");
			}
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
					if (panel is FloatingThingFinderViewModel) continue;
					// Existing archive panels restore only when docked; the generator is safe to restore floating.
					if (panel.DockState == "Floating" && panel is not FloatingLooktypeGeneratorViewModel) continue;

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

					model.Assets.Panels.Add(state);
				}

				string toml = TomlSerializer.Serialize(model);
				File.WriteAllText(AppStatePath, toml);
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
					// Existing archive panels restore only when docked; the generator may also restore floating.
					if ((panelState.DockState == "Floating" && panelState.Type != "Looktype") || string.IsNullOrEmpty(panelState.DockState)) continue;

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
