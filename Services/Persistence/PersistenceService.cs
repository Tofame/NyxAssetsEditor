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
			public string ThingEditorGridColor { get; set; } = "#B4808080";
			public int ThingEditorGridLineWidth { get; set; } = 1;
			public string ThingEditorDragGridColor { get; set; } = "#B4FF69B4";
			public int ThingEditorDragGridLineWidth { get; set; } = 1;
			public string ThingEditorDragHighlightColor { get; set; } = "#803A7BD5";
		}

		public class AppStateTomlModel
		{
			public AssetsStateModel Assets { get; set; } = new AssetsStateModel();
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
			public string Type { get; set; } = ""; // "Sprite" or "Things"
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

			// Sprite-specific
			public bool UseTransparentPixels { get; set; } = true;
			public bool UseExtendedSpriteIds { get; set; } = true;

			// Things-specific
			public bool UseExtendedThingIds { get; set; } = true;
			public bool UseFrameAnimations { get; set; } = true;
			public bool UseFrameGroups { get; set; } = true;
			public string LinkedSpriteFilePath { get; set; } = "";
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
							model.ThingEditorDragHighlightColor);
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
					ThingEditorGridColor = SettingsViewModel.ThingEditorGridColor,
					ThingEditorGridLineWidth = SettingsViewModel.ThingEditorGridLineWidth,
					ThingEditorDragGridColor = SettingsViewModel.ThingEditorDragGridColor,
					ThingEditorDragGridLineWidth = SettingsViewModel.ThingEditorDragGridLineWidth,
					ThingEditorDragHighlightColor = SettingsViewModel.ThingEditorDragHighlightColor
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

				foreach (var panel in assetsVm.ActivePanels)
				{
					// Only persist docked panels
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

		public static void LoadAppState(AssetsViewModel assetsVm, SpriteRenderer spriteRenderer)
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

				foreach (var panelState in model.Assets.Panels)
				{
					// Only restore docked panels
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
							UseExtendedSpriteIds = panelState.UseExtendedSpriteIds
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
							UseFrameGroups = panelState.UseFrameGroups
						};

						assetsVm.RestorePanel(panel);
						thingsPanels.Add((panelState, panel));
					}
				}

				foreach (var (panelState, panel) in spritePanels)
				{
					if (!string.IsNullOrEmpty(panelState.FilePath) && File.Exists(panelState.FilePath))
					{
						try
						{
							panel.LoadArchive(panelState.FilePath);
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
							panel.LoadArchive(panelState.FilePath, useLastLoadedSprite: false);
							panel.CurrentPage = panelState.CurrentPage;
						}
						catch (Exception ex)
						{
							Debug.WriteLine($"Failed to load dat/things from state: {ex.Message}");
						}
					}
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
	}
}
