using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NyxAssets.Sprites;
using NyxAssets.Things;
using NyxAssets.Things.Frames;
using NyxAssetsEditor.Services.Exchange;
using NyxAssetsEditor.Services.Things;
using NyxAssetsEditor.Services.Rendering;
using NyxAssetsEditor.ViewModels.Core;
using NyxAssetsEditor.ViewModels.Pages;
using Avalonia.Media;

namespace NyxAssetsEditor.ViewModels.ArchiveLoaders;

public partial class FloatingThingEditorViewModel : PanelViewModelBase
{
	public bool IsEmbedded { get; set; }
	public bool ShowEditorTitleBar => !IsEmbedded;
	public bool ShowEditorResizeHandles => !IsEmbedded && ShowResizeHandles;
	public Action<ThingType>? BatchSaveRequested { get; set; }
	public Action? BatchCancelRequested { get; set; }
	public bool UseDetachedThing { get; }
	public FloatingMultiThingEditorViewModel? BatchHost { get; set; }
	public bool IsBatchEditor => BatchHost != null;
	public HashSet<string> BatchTouchedProperties { get; } = new(StringComparer.Ordinal);
	public void SetBatchOverride(string propertyName, bool enabled)
	{
		if (!IsBatchEditor) return;
		if (enabled) BatchTouchedProperties.Add(propertyName);
		else BatchTouchedProperties.Remove(propertyName);
		IsDirty = BatchTouchedProperties.Count > 0;
	}
	private readonly SpriteRenderer _renderer = new();
	private WriteableBitmap? _appearanceImage;
	private int _selectedFrameGroupIndex;
	private int _selectedLayer;
	private int _selectedFrame;
	private uint _viewPatternX;
	private uint _viewPatternY;
	private uint _viewPatternZ;
	private Direction4 _outfitDirection = Direction4.South;
	private Direction8 _missileDirection = Direction8.South;
	private bool _showGrid;
	private bool _showCropSize;
	private int _selectedAnimationMode;
	private bool _isPingPongStrategy;
	private ThingType _thing = null!;
	private bool _patternFieldGuard;
	private int _tileWidth = 1;
	private int _tileHeight = 1;
	private int _cropSize = 32;
	private int _layerCount = 1;
	private int _patternXCount = 1;
	private int _patternYCount = 1;
	private int _patternZCount = 1;
	private int _frameCount = 1;
	private int _animationLoopCount = 1;
	private int _animationStartFrame;
	private bool _isAnimationPlaying;
	private int _animationDirection = 1;
	private int _frameBeforePreview;
	private DispatcherTimer? _animationTimer;
	private int _appearancePixelWidth;
	private int _appearancePixelHeight;
	private bool _showAddSpriteConfirmation;
	private string _addSpriteConfirmationText = string.Empty;
	private FloatingSpriteLoaderViewModel? _pendingSpriteSource;
	private uint _pendingSpriteId;
	private double _pendingDropX;
	private double _pendingDropY;
	private bool _isAppearanceDragHover;
	private ThingAppearanceSlot? _hoverSlot;

	private ThingType _originalThing = null!;
	private bool _isDirty;
	private bool _showPromptOverlay;
	private string _promptTitle = string.Empty;
	private string _promptText = string.Empty;
	private System.Threading.Tasks.TaskCompletionSource<PromptResult>? _promptTcs;

	private int _selectedTabIndex;
	public int SelectedTabIndex
	{
		get => _selectedTabIndex;
		set => SetProperty(ref _selectedTabIndex, value);
	}

	private Dictionary<string, string> _loadedFlags = new(StringComparer.Ordinal);
	private Dictionary<string, string> _loadedFlagDescriptions = new(StringComparer.Ordinal);
	private Dictionary<string, PropertyConfig> _loadedProperties = new(StringComparer.Ordinal);

	public class FlagVisibilityMap
	{
		private readonly FloatingThingEditorViewModel _vm;
		public FlagVisibilityMap(FloatingThingEditorViewModel vm) => _vm = vm;
		public bool this[string key] => _vm._loadedFlags.ContainsKey(key);
	}

	public class FlagLabelMap
	{
		private readonly FloatingThingEditorViewModel _vm;
		private readonly Dictionary<string, string> _defaults;
		public FlagLabelMap(FloatingThingEditorViewModel vm, Dictionary<string, string> defaults)
		{
			_vm = vm;
			_defaults = defaults;
		}
		public string this[string key] => _vm._loadedFlags.TryGetValue(key, out var label) ? label : (_defaults.TryGetValue(key, out var def) ? def : key);
	}

	public class FlagTooltipMap
	{
		private readonly FloatingThingEditorViewModel _vm;
		public FlagTooltipMap(FloatingThingEditorViewModel vm) => _vm = vm;
		public string? this[string key]
		{
			get
			{
				if (_vm._loadedFlagDescriptions.TryGetValue(key, out var desc) && !string.IsNullOrWhiteSpace(desc))
					return desc;
				if (_vm._loadedProperties.TryGetValue(key, out var p) && !string.IsNullOrWhiteSpace(p.description))
					return p.description;
				if (_defaultFlagDescriptions.TryGetValue(key, out var defDesc))
					return defDesc;
				return null;
			}
		}
	}

	public FlagVisibilityMap FlagVisibility => new(this);
	public FlagLabelMap FlagLabel => new(this, _defaultLabels);
	public FlagTooltipMap FlagTooltip => new(this);

	public class FlagConfig
	{
		public byte id { get; set; }
		public string? label { get; set; }
		public string? description { get; set; }
	}

	public class PropertyConfig
	{
		public string? label { get; set; }
		public string? description { get; set; }
	}

	public class PropertiesTomlModel
	{
		public Dictionary<string, PropertyConfig>? properties { get; set; }
	}

	public class FlagsTomlModel
	{
		public Dictionary<string, FlagConfig>? flags { get; set; }
	}

	public enum PromptResult
	{
		Save,
		DontSave,
		Cancel
	}

	public bool IsDirty
	{
		get => _isDirty;
		set
		{
			if (SetProperty(ref _isDirty, value))
			{
				OnPropertyChanged(nameof(CanSave));
				OnPropertyChanged(nameof(CanCancel));
			}
		}
	}

	public bool CanSave => IsDirty;
	public bool CanCancel => IsDirty;
	public string SaveButtonText => BatchSaveRequested != null ? "Save All" : "Save";

	public bool ShowPromptOverlay
	{
		get => _showPromptOverlay;
		set => SetProperty(ref _showPromptOverlay, value);
	}

	public string PromptTitle
	{
		get => _promptTitle;
		set => SetProperty(ref _promptTitle, value);
	}

	public string PromptText
	{
		get => _promptText;
		set => SetProperty(ref _promptText, value);
	}

	public void ShowPrompt(string title, string text, System.Threading.Tasks.TaskCompletionSource<PromptResult> tcs)
	{
		PromptTitle = title;
		PromptText = text;
		_promptTcs = tcs;
		ShowPromptOverlay = true;
	}

	[RelayCommand]
	public void PromptSave()
	{
		ShowPromptOverlay = false;
		_promptTcs?.SetResult(PromptResult.Save);
	}

	[RelayCommand]
	public void PromptDontSave()
	{
		ShowPromptOverlay = false;
		_promptTcs?.SetResult(PromptResult.DontSave);
	}

	[RelayCommand]
	public void PromptCancel()
	{
		ShowPromptOverlay = false;
		_promptTcs?.SetResult(PromptResult.Cancel);
	}

	public bool ShowInformationBoxes => SettingsViewModel.ShowInformationBoxes;

	public FloatingThingEditorViewModel(FloatingThingsLoaderViewModel source, ThingType thing, bool useDetachedThing = false)
	{
		SourcePanel = source;
		UseDetachedThing = useDetachedThing;
		RequestClose += _ =>
		{
			StopAnimationPreview(restoreFrame: false);
			SettingsViewModel.ThingEditorAppearanceSettingsChanged -= OnAppearanceSettingsChanged;
			SettingsViewModel.ShowInformationBoxesChanged -= OnShowInformationBoxesChanged;
		};
		SettingsViewModel.ThingEditorAppearanceSettingsChanged += OnAppearanceSettingsChanged;
		SettingsViewModel.ShowInformationBoxesChanged += OnShowInformationBoxesChanged;
		LoadThing(thing);
		PanelWidth = 540;
		ContentHeight = 680;
		PositionX = source.PositionX + 40;
		PositionY = source.PositionY + 40;
	}

	private void OnShowInformationBoxesChanged()
	{
		OnPropertyChanged(nameof(ShowInformationBoxes));
	}

	public void RefreshPatternBindings() => SyncPatternFieldsFromGroup();

	public void LoadThing(ThingType thing)
	{
		StopAnimationPreview(restoreFrame: false);
		_originalThing = UseDetachedThing ? thing : SourcePanel.GetThingType(thing.Id) ?? thing;
		_thing = Services.Exchange.ThingCloner.Clone(_originalThing, _originalThing.Id);
		_isDirty = false;
		OnPropertyChanged(nameof(IsDirty));
		OnPropertyChanged(nameof(CanSave));
		OnPropertyChanged(nameof(CanCancel));

		_selectedFrameGroupIndex = 0;
		_selectedFrame = 0;
		_selectedLayer = 0;
		_viewPatternX = 0;
		_viewPatternY = 0;
		_viewPatternZ = 0;
		_outfitDirection = Direction4.South;
		_missileDirection = Direction8.South;

		LoadProtocolFlags();
		LoadCustomFlagSchema();
		NotifyThingProperties();
		if (!IsItem && _selectedTabIndex == 2)
		{
			SelectedTabIndex = 0;
		}
		NotifyAppearanceControls();
		OnPropertyChanged(nameof(SelectedFrameGroupIndex));
		OnPropertyChanged(nameof(FrameGroupDisplay));

		SyncPatternFieldsFromGroup();
		SyncViewPatternsFromDirection();
		SyncAnimationFieldsFromGroup();
		NotifySliderDisplays();
		RefreshAppearance();
	}

	public FloatingThingsLoaderViewModel SourcePanel { get; }
	public ThingType Thing => _thing;

	public uint ThingId => Thing.Id;
	public ThingKind Kind => Thing.Kind;
	public string Title => $"Thing Editor #{SourcePanel.GetDisplayedId(ThingId)}";
	public bool ImprovedAnimations => SourcePanel.UseFrameAnimations;
	public bool OutfitFrameGroupsEnabled => SourcePanel.UseFrameGroups;

	public bool HasPatterns => Thing.GetFrameGroup(SelectedFrameGroupIndex) != null;

	public ThingFrameGroup CurrentFrameGroup =>
		Thing.GetFrameGroup(SelectedFrameGroupIndex)
		?? throw new InvalidOperationException($"Thing #{Thing.Id} has no frame group at index {SelectedFrameGroupIndex}.");

	public bool IsOutfit => Kind == ThingKind.Outfit;
	public bool IsMissile => Kind == ThingKind.Missile;
	public bool IsItem => Kind == ThingKind.Item;
	public bool IsEffect => Kind == ThingKind.Effect;
	public bool ShowOutfitDirections => IsOutfit && !ShowAllOutfitDirections;

	private bool _showAllOutfitDirections;
	public bool ShowAllOutfitDirections
	{
		get => _showAllOutfitDirections;
		set
		{
			if (SetProperty(ref _showAllOutfitDirections, value))
			{
				OnPropertyChanged(nameof(ShowOutfitDirections));
				RefreshAppearance();
			}
		}
	}

	private DispatcherTimer? _rotateTimer;
	private bool _autoRotate;
	public bool AutoRotate
	{
		get => _autoRotate;
		set
		{
			if (SetProperty(ref _autoRotate, value))
			{
				if (value) StartRotateTimer();
				else StopRotateTimer();
			}
		}
	}

	private int _rotateSpeedMs = 500;
	public int RotateSpeedMs
	{
		get => _rotateSpeedMs;
		set
		{
			if (SetProperty(ref _rotateSpeedMs, value))
			{
				if (AutoRotate)
				{
					StopRotateTimer();
					StartRotateTimer();
				}
			}
		}
	}

	private void StartRotateTimer()
	{
		_rotateTimer?.Stop();
		_rotateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(50, _rotateSpeedMs)) };
		_rotateTimer.Tick += OnRotateTimerTick;
		_rotateTimer.Start();
	}

	private void StopRotateTimer()
	{
		if (_rotateTimer != null)
		{
			_rotateTimer.Tick -= OnRotateTimerTick;
			_rotateTimer.Stop();
			_rotateTimer = null;
		}
	}

	private void OnRotateTimerTick(object? sender, EventArgs e)
	{
		if (!AutoRotate) return;
		var nextDir = (Direction4)(((int)_outfitDirection + 1) % 4);
		SetOutfitDirection(nextDir);
	}

	private bool _useCustomPlaySpeed;
	public bool UseCustomPlaySpeed
	{
		get => _useCustomPlaySpeed;
		set
		{
			if (SetProperty(ref _useCustomPlaySpeed, value))
			{
				if (IsAnimationPlaying)
				{
					ArmAnimationTimer(SelectedFrame);
				}
			}
		}
	}

	private int _playSpeedMs = 100;
	public int PlaySpeedMs
	{
		get => _playSpeedMs;
		set
		{
			if (SetProperty(ref _playSpeedMs, value))
			{
				if (IsAnimationPlaying)
				{
					ArmAnimationTimer(SelectedFrame);
				}
			}
		}
	}
	public bool ShowMissileDirections => false;
	public bool ShowLayerSlider => CurrentFrameGroup.Layers > 1;
	public bool UsesOutfitFrameGroups => IsOutfit && OutfitFrameGroupsEnabled && Thing.FrameGroups.Count > 1;
	public bool ShowFrameSlider => CurrentFrameGroup.Frames > 1 && (!UsesOutfitFrameGroups || SelectedFrameGroupIndex > 0);
	public bool IsAnimationPlaying
	{
		get => _isAnimationPlaying;
		private set
		{
			if (SetProperty(ref _isAnimationPlaying, value))
			{
				OnPropertyChanged(nameof(AutoPlay));
			}
		}
	}

	public bool AutoPlay
	{
		get => IsAnimationPlaying;
		set
		{
			if (value) StartAnimationPreview();
			else StopAnimationPreview();
			OnPropertyChanged(nameof(AutoPlay));
		}
	}
	public bool ShowPatternGrid => !IsOutfit && !IsMissile;
	public bool ShowPatternXSlider => false;
	public bool ShowPatternYSlider => false;
	public bool ShowPatternZSlider => CurrentFrameGroup.PatternZ > 1;
	public bool ShowAddonSlider => IsOutfit && CurrentFrameGroup.PatternY > 1;
	public bool ShowFrameGroupSlider => UsesOutfitFrameGroups;
	public bool ShowAnimationSection => ImprovedAnimations && ShowFrameSlider;
	public bool ShowDurationEditors => ImprovedAnimations && ShowAnimationSection && ShowDurationEditorsForCategory;

	public string LayerDisplay => $"{SelectedLayer + 1}/{Math.Max(1, (int)CurrentFrameGroup.Layers)}";
	public string FrameDisplay => $"{SelectedFrame + 1}/{Math.Max(1, (int)CurrentFrameGroup.Frames)}";
	public string FrameGroupDisplay => SelectedFrameGroupIndex <= 0 ? "Idle/Stand" : "Walking";
	public string PatternXDisplay => $"{ViewPatternXIndex + 1}/{Math.Max(1, (int)CurrentFrameGroup.PatternX)}";
	public string PatternYDisplay => $"{ViewPatternYIndex + 1}/{Math.Max(1, (int)CurrentFrameGroup.PatternY)}";
	public string PatternZDisplay => $"{ViewPatternZIndex + 1}/{Math.Max(1, (int)CurrentFrameGroup.PatternZ)}";

	private bool ShowDurationEditorsForCategory =>
		IsItem || IsEffect
		|| (IsOutfit && (Thing.AnimateAlways || Thing.FrameGroups.Count > 1) && SelectedFrameGroupIndex == 0);

	public int LayerMaximum => Math.Max(0, (int)CurrentFrameGroup.Layers - 1);
	public int FrameMaximum => Math.Max(0, (int)CurrentFrameGroup.Frames - 1);
	public int FrameGroupMaximum => Math.Max(0, Thing.FrameGroups.Count - 1);
	public int PatternXMaximum => Math.Max(0, (int)CurrentFrameGroup.PatternX - 1);
	public int PatternYMaximum => Math.Max(0, (int)CurrentFrameGroup.PatternY - 1);
	public int PatternZMaximum => Math.Max(0, (int)CurrentFrameGroup.PatternZ - 1);
	public int AddonMaximum => PatternYMaximum;
	public int StartFrameMaximum => FrameMaximum;
	public int LoopCountMaximum => 999;

	public WriteableBitmap? AppearanceImage
	{
		get => _appearanceImage;
		private set => SetProperty(ref _appearanceImage, value);
	}

	public int AppearancePixelWidth => _appearancePixelWidth;
	public int AppearancePixelHeight => _appearancePixelHeight;

	public bool ShowAddSpriteConfirmation
	{
		get => _showAddSpriteConfirmation;
		private set => SetProperty(ref _showAddSpriteConfirmation, value);
	}

	public string AddSpriteConfirmationText
	{
		get => _addSpriteConfirmationText;
		private set => SetProperty(ref _addSpriteConfirmationText, value);
	}

	public void HandleSpriteDrop(FloatingSpriteLoaderViewModel sourcePanel, uint spriteId, double dropX, double dropY)
	{
		ClearAppearanceDragHover();

		if (sourcePanel is not { IsArchiveLoaded: true })
			return;

		if (spriteId == 0)
		{
			AssignSpriteToDropTarget(0, dropX, dropY);
			return;
		}

		if (SourcePanel.LinkedSpritePanel == null)
		{
			if (NyxAssetsEditor.ViewModels.Common.ArchiveFormatHelper.AreCompatible(sourcePanel.ArchiveFormat, SourcePanel.ArchiveFormat))
			{
				SourcePanel.LinkedSpritePanel = sourcePanel;
				SourcePanel.NotifySpriteLinkChanged();
				RefreshAppearance();
			}
		}

		if (SourcePanel.GetActiveSpriteLoader() == null)
			return;

		var linkedPanel = SourcePanel.LinkedSpritePanel;
		if (linkedPanel == null)
			return;

		if (ReferenceEquals(linkedPanel, sourcePanel))
		{
			AssignSpriteToDropTarget(spriteId, dropX, dropY);
			return;
		}

		_pendingSpriteSource = sourcePanel;
		_pendingSpriteId = spriteId;
		_pendingDropX = dropX;
		_pendingDropY = dropY;

		var linkedName = linkedPanel.FilePath;
		var sourceName = string.IsNullOrWhiteSpace(sourcePanel.FilePath) || sourcePanel.FilePath == "No archive loaded"
			? "another sprite viewer"
			: sourcePanel.FilePath;

		AddSpriteConfirmationText =
			$"Sprite #{spriteId} is from {sourceName}, not from {linkedName} linked to this things archive.\n\n" +
			"Add a copy of this sprite to the linked archive and assign it to the thing?";
		ShowAddSpriteConfirmation = true;
	}

	public void UpdateAppearanceDragHover(double dropX, double dropY)
	{
		if (_appearancePixelWidth <= 0 || _appearancePixelHeight <= 0)
			return;

		var slot = ThingAppearanceDropTarget.Resolve(this, dropX, dropY, _appearancePixelWidth, _appearancePixelHeight);
		if (_isAppearanceDragHover && Nullable.Equals(_hoverSlot, slot))
			return;

		_isAppearanceDragHover = true;
		_hoverSlot = slot;
		RefreshAppearance();
	}

	public void ClearAppearanceDragHover()
	{
		if (!_isAppearanceDragHover && _hoverSlot == null)
			return;

		_isAppearanceDragHover = false;
		_hoverSlot = null;
		RefreshAppearance();
	}

	private void OnAppearanceSettingsChanged() => RefreshAppearance();

	[RelayCommand]
	private void ConfirmAddSprite()
	{
		ShowAddSpriteConfirmation = false;

		var linkedPanel = SourcePanel.LinkedSpritePanel;
		if (linkedPanel == null || _pendingSpriteSource == null || _pendingSpriteId < 1)
		{
			ClearPendingSpriteDrop();
			return;
		}

		try
		{
			var pixels = _pendingSpriteSource.Loader.LoadSpritePixels(_pendingSpriteId);
			var newId = linkedPanel.Loader.AddNewSprite();
			linkedPanel.Loader.SetSpritePixels(newId, pixels);
			linkedPanel.NotifyExternalArchiveMutation();
			linkedPanel.HasSavedChanges = true;
			AssignSpriteToDropTarget(newId, _pendingDropX, _pendingDropY);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Failed to add dragged sprite: {ex.Message}");
		}

		ClearPendingSpriteDrop();
	}

	[RelayCommand]
	private void CancelAddSprite()
	{
		ShowAddSpriteConfirmation = false;
		ClearPendingSpriteDrop();
	}

	[RelayCommand]
	private void InferCropSize()
	{
		var loader = SourcePanel.GetActiveSpriteLoader();
		if (loader == null) return;

		var fg = CurrentFrameGroup;
		var inferredSize = ThingFrameGroupEditor.InferCropSize(fg, id =>
		{
			try { return loader.LoadSpritePixels(id); }
			catch { return null; }
		});

		CropSize = inferredSize;
	}

	private void ClearPendingSpriteDrop()
	{
		_pendingSpriteSource = null;
		_pendingSpriteId = 0;
		_pendingDropX = 0;
		_pendingDropY = 0;
	}

	private void AssignSpriteToDropTarget(uint spriteId, double dropX, double dropY)
	{
		if (_appearancePixelWidth <= 0 || _appearancePixelHeight <= 0)
			return;

		var slot = ThingAppearanceDropTarget.Resolve(this, dropX, dropY, _appearancePixelWidth, _appearancePixelHeight);
		if (slot == null)
			return;

		var fg = CurrentFrameGroup;
		var index = fg.GetSpriteIndex(
			slot.Value.InnerW,
			slot.Value.InnerH,
			(uint)SelectedLayer,
			slot.Value.PatternX,
			slot.Value.PatternY,
			_viewPatternZ,
			(uint)SelectedFrame);

		if (index >= fg.SpriteIds.Length)
			return;

		fg.SpriteIds[index] = spriteId;
		ApplyToCatalog();
		RefreshAppearance();
	}

	public int SelectedFrameGroupIndex
	{
		get => _selectedFrameGroupIndex;
		set
		{
			if (!SetProperty(ref _selectedFrameGroupIndex, Math.Clamp(value, 0, FrameGroupMaximum)))
				return;

			StopAnimationPreview(restoreFrame: false);
			_selectedLayer = Math.Clamp(_selectedLayer, 0, LayerMaximum);
			_selectedFrame = 0;
			OnPropertyChanged(nameof(FrameGroupDisplay));
			SyncViewPatternsFromDirection();
			SyncPatternFieldsFromGroup();
			NotifyAppearanceControls();
			NotifySliderDisplays();
			SyncAnimationFieldsFromGroup();
			RefreshAppearance();
		}
	}

	public int SelectedLayer
	{
		get => _selectedLayer;
		set
		{
			if (!SetProperty(ref _selectedLayer, Math.Clamp(value, 0, LayerMaximum)))
				return;
			NotifySliderDisplays();
			RefreshAppearance();
		}
	}

	public int SelectedFrame
	{
		get => _selectedFrame;
		set
		{
			if (!SetProperty(ref _selectedFrame, Math.Clamp(value, 0, FrameMaximum)))
				return;
			OnPropertyChanged(nameof(MinimumDuration));
			OnPropertyChanged(nameof(MaximumDuration));
			NotifySliderDisplays();
			RefreshAppearance();
		}
	}

	public int ViewPatternXIndex
	{
		get => (int)_viewPatternX;
		set
		{
			var clamped = Math.Clamp(value, 0, PatternXMaximum);
			if ((int)_viewPatternX == clamped)
				return;
			_viewPatternX = (uint)clamped;
			OnPropertyChanged(nameof(ViewPatternXIndex));
			NotifySliderDisplays();
			RefreshAppearance();
		}
	}

	public int ViewPatternYIndex
	{
		get => (int)_viewPatternY;
		set
		{
			var clamped = Math.Clamp(value, 0, PatternYMaximum);
			if ((int)_viewPatternY == clamped)
				return;
			_viewPatternY = (uint)clamped;
			OnPropertyChanged(nameof(ViewPatternYIndex));
			NotifySliderDisplays();
			RefreshAppearance();
		}
	}

	public int ViewPatternZIndex
	{
		get => (int)_viewPatternZ;
		set
		{
			var clamped = Math.Clamp(value, 0, PatternZMaximum);
			if ((int)_viewPatternZ == clamped)
				return;
			_viewPatternZ = (uint)clamped;
			OnPropertyChanged(nameof(ViewPatternZIndex));
			NotifySliderDisplays();
			RefreshAppearance();
		}
	}

	public bool ShowGrid
	{
		get => _showGrid;
		set
		{
			if (!SetProperty(ref _showGrid, value))
				return;
			RefreshAppearance();
		}
	}

	public bool ShowCropSize
	{
		get => _showCropSize;
		set
		{
			if (!SetProperty(ref _showCropSize, value))
				return;
			RefreshAppearance();
		}
	}

	public int TileWidth
	{
		get => _tileWidth;
		set
		{
			if (_patternFieldGuard || _tileWidth == value)
				return;
			ApplyPatternChange(g => g.Width = ClampPattern(value, 32));
		}
	}

	public int TileHeight
	{
		get => _tileHeight;
		set
		{
			if (_patternFieldGuard || _tileHeight == value)
				return;
			ApplyPatternChange(g => g.Height = ClampPattern(value, 32));
		}
	}

	public int CropSize
	{
		get => _cropSize;
		set
		{
			if (_patternFieldGuard || _cropSize == value)
				return;
			ApplyPatternChange(g => g.ExactSize = ClampPattern(value, 64));
		}
	}

	public int LayerCount
	{
		get => _layerCount;
		set
		{
			if (_patternFieldGuard || _layerCount == value)
				return;
			ApplyPatternChange(g => g.Layers = ClampPattern(value, 16));
		}
	}

	public int PatternXCount
	{
		get => _patternXCount;
		set
		{
			if (_patternFieldGuard || _patternXCount == value)
				return;
			ApplyPatternChange(g => g.PatternX = ClampPattern(value, 32));
		}
	}

	public int PatternYCount
	{
		get => _patternYCount;
		set
		{
			if (_patternFieldGuard || _patternYCount == value)
				return;
			ApplyPatternChange(g => g.PatternY = ClampPattern(value, 32));
		}
	}

	public int PatternZCount
	{
		get => _patternZCount;
		set
		{
			if (_patternFieldGuard || _patternZCount == value)
				return;
			ApplyPatternChange(g => g.PatternZ = ClampPattern(value, 16));
		}
	}

	public int FrameCount
	{
		get => _frameCount;
		set
		{
			if (_patternFieldGuard || _frameCount == value)
				return;
			var frames = ClampPattern(value, 60);
			ApplyPatternChange(g =>
			{
				g.Frames = frames;
				var defaults = SettingsViewModel.GetDefaultAnimationDurationMs(Kind);
				ThingFrameGroupEditor.EnsureFrameTimings(g, defaults, defaults);
			});
			SyncAnimationFieldsFromGroup();
		}
	}

	private static uint ClampPattern(int value, uint max) =>
		(uint)Math.Clamp(value, 1, (int)max);

	public int SelectedAnimationMode
	{
		get => _selectedAnimationMode;
		set
		{
			if (!SetProperty(ref _selectedAnimationMode, value))
				return;
			CurrentFrameGroup.AnimationMode = (byte)value;
			ApplyToCatalog();
		}
	}

	public bool IsPingPongStrategy
	{
		get => _isPingPongStrategy;
		set
		{
			if (!SetProperty(ref _isPingPongStrategy, value))
				return;
			CurrentFrameGroup.LoopCount = value ? -1 : Math.Max(0, _animationLoopCount);
			OnPropertyChanged(nameof(AnimationLoopCount));
			OnPropertyChanged(nameof(FrameStrategyIndex));
			OnPropertyChanged(nameof(ShowLoopCountEditor));
			ApplyToCatalog();
		}
	}

	public bool ShowLoopCountEditor => ShowAnimationSection && !IsPingPongStrategy;

	public int AnimationLoopCount
	{
		get => _animationLoopCount;
		set
		{
			if (!SetProperty(ref _animationLoopCount, Math.Max(0, value)))
				return;
			if (!IsPingPongStrategy)
			{
				CurrentFrameGroup.LoopCount = _animationLoopCount;
				ApplyToCatalog();
			}
		}
	}

	public int AnimationStartFrame
	{
		get => _animationStartFrame;
		set
		{
			if (!SetProperty(ref _animationStartFrame, value))
				return;
			CurrentFrameGroup.StartFrame = value;
			ApplyToCatalog();
		}
	}

	public decimal MinimumDuration
	{
		get => GetCurrentTiming()?.MinimumMilliseconds ?? 0;
		set
		{
			if (GetCurrentTiming() is not { } timing)
				return;
			var max = Math.Max((uint)value, timing.MaximumMilliseconds);
			CurrentFrameGroup.FrameTimings![SelectedFrame] = new AnimationFrameTiming((uint)value, max);
			OnPropertyChanged(nameof(MaximumDuration));
			ApplyToCatalog();
		}
	}

	public decimal MaximumDuration
	{
		get => GetCurrentTiming()?.MaximumMilliseconds ?? 0;
		set
		{
			if (GetCurrentTiming() is not { } timing)
				return;
			var min = Math.Min((uint)value, timing.MinimumMilliseconds);
			CurrentFrameGroup.FrameTimings![SelectedFrame] = new AnimationFrameTiming(min, (uint)value);
			OnPropertyChanged(nameof(MinimumDuration));
			ApplyToCatalog();
		}
	}

	public ObservableCollection<string> AnimationModes { get; } = new() { "Asynchronous", "Synchronous" };
	public ObservableCollection<string> FrameStrategies { get; } = new() { "Loop", "Ping-pong" };

	[RelayCommand]
	private void SetDirectionNorth() => SetOutfitDirection(Direction4.North);

	[RelayCommand]
	private void SetDirectionEast() => SetOutfitDirection(Direction4.East);

	[RelayCommand]
	private void SetDirectionSouth() => SetOutfitDirection(Direction4.South);

	[RelayCommand]
	private void SetDirectionWest() => SetOutfitDirection(Direction4.West);

	[RelayCommand]
	private void SetMissileDirection(string direction)
	{
		if (!Enum.TryParse<Direction8>(direction, out var parsed))
			return;
		_missileDirection = parsed;
		SyncViewPatternsFromDirection();
		RefreshAppearance();
	}

	public bool CanGenerateMissileOrthogonalDirections => GetMissileOrthogonalSourceDirection() != null;
	public bool CanGenerateMissileDiagonalDirections => GetMissileDiagonalSourceDirection() != null;

	private bool DirectionHasSprites(ThingFrameGroup fg, uint px, uint py)
	{
		for (uint f = 0; f < fg.Frames; f++)
		{
			if (fg.GetSpriteId(0, 0, (uint)SelectedLayer, px, py, _viewPatternZ, f) > 0)
				return true;
		}
		return false;
	}

	private Direction8? GetMissileOrthogonalSourceDirection()
	{
		if (!IsMissile) return null;
		var fg = CurrentFrameGroup;
		Span<Direction8> orthos = stackalloc Direction8[] { Direction8.North, Direction8.East, Direction8.South, Direction8.West };
		Direction8? source = null;
		int count = 0;
		foreach (var dir in orthos)
		{
			var (px, py) = MissileDirectionPatterns.GetPattern(dir);
			if (DirectionHasSprites(fg, px, py))
			{
				count++;
				source = dir;
			}
		}
		return count == 1 ? source : null;
	}

	private Direction8? GetMissileDiagonalSourceDirection()
	{
		if (!IsMissile) return null;
		var fg = CurrentFrameGroup;
		Span<Direction8> diags = stackalloc Direction8[] { Direction8.NorthWest, Direction8.NorthEast, Direction8.SouthEast, Direction8.SouthWest };
		Direction8? source = null;
		int count = 0;
		foreach (var dir in diags)
		{
			var (px, py) = MissileDirectionPatterns.GetPattern(dir);
			if (DirectionHasSprites(fg, px, py))
			{
				count++;
				source = dir;
			}
		}
		return count == 1 ? source : null;
	}

	[RelayCommand(CanExecute = nameof(CanGenerateMissileOrthogonalDirections))]
	private void GenerateMissileOrthogonalDirections()
	{
		var sourceDir = GetMissileOrthogonalSourceDirection();
		if (sourceDir == null) return;
		GenerateMissileRotations(sourceDir.Value, isOrthogonal: true);
	}

	[RelayCommand(CanExecute = nameof(CanGenerateMissileDiagonalDirections))]
	private void GenerateMissileDiagonalDirections()
	{
		var sourceDir = GetMissileDiagonalSourceDirection();
		if (sourceDir == null) return;
		GenerateMissileRotations(sourceDir.Value, isOrthogonal: false);
	}

	private void GenerateMissileRotations(Direction8 sourceDir, bool isOrthogonal)
	{
		var loader = SourcePanel.GetActiveSpriteLoader();
		var linkedPanel = SourcePanel.LinkedSpritePanel;
		if (loader == null || linkedPanel == null) return;

		var fg = CurrentFrameGroup;
		var (srcPx, srcPy) = MissileDirectionPatterns.GetPattern(sourceDir);

		Direction8[] targets = isOrthogonal
			? new[] { Direction8.North, Direction8.East, Direction8.South, Direction8.West }
			: new[] { Direction8.NorthWest, Direction8.NorthEast, Direction8.SouthEast, Direction8.SouthWest };

		int sourceIdx = Array.IndexOf(targets, sourceDir);
		if (sourceIdx < 0) return;

		for (uint frame = 0; frame < fg.Frames; frame++)
		{
			var srcSpriteId = fg.GetSpriteId(0, 0, (uint)SelectedLayer, srcPx, srcPy, _viewPatternZ, frame);
			if (srcSpriteId < 1) continue;

			byte[] srcRgba;
			try { srcRgba = loader.LoadSpritePixels(srcSpriteId); }
			catch { continue; }
			if (srcRgba == null || srcRgba.Length != SpritePixelCodec.RgbaBufferLength) continue;

			for (int i = 0; i < targets.Length; i++)
			{
				if (i == sourceIdx) continue;

				int rotSteps = (i - sourceIdx + 4) % 4;
				byte[] rotatedRgba = RotateRgba90(srcRgba, rotSteps);

				var newId = linkedPanel.Loader.AddNewSprite();
				linkedPanel.Loader.SetSpritePixels(newId, rotatedRgba);

				var targetDir = targets[i];
				var (targetPx, targetPy) = MissileDirectionPatterns.GetPattern(targetDir);
				var index = fg.GetSpriteIndex(0, 0, (uint)SelectedLayer, targetPx, targetPy, _viewPatternZ, frame);
				if (index < fg.SpriteIds.Length)
				{
					fg.SpriteIds[index] = newId;
				}
			}
		}

		linkedPanel.NotifyExternalArchiveMutation();
		linkedPanel.HasSavedChanges = true;
		ApplyToCatalog();
		RefreshAppearance();
	}

	private static byte[] RotateRgba90(byte[] src, int steps)
	{
		steps = (steps % 4 + 4) % 4;
		if (steps == 0) return (byte[])src.Clone();

		int edge = SpritePixelCodec.SpriteEdgeLength;
		byte[] dest = new byte[src.Length];

		for (int y = 0; y < edge; y++)
		{
			for (int x = 0; x < edge; x++)
			{
				int srcIdx = (y * edge + x) * 4;
				int newX = x;
				int newY = y;

				switch (steps)
				{
					case 1: // 90° clockwise
						newX = edge - 1 - y;
						newY = x;
						break;
					case 2: // 180°
						newX = edge - 1 - x;
						newY = edge - 1 - y;
						break;
					case 3: // 270° clockwise (90° counter-clockwise)
						newX = y;
						newY = edge - 1 - x;
						break;
				}

				int destIdx = (newY * edge + newX) * 4;
				Buffer.BlockCopy(src, srcIdx, dest, destIdx, 4);
			}
		}

		return dest;
	}

	[RelayCommand]
	private void ApplyDefaultDurations()
	{
		var ms = SettingsViewModel.GetDefaultAnimationDurationMs(Kind);
		SetCurrentTiming(new AnimationFrameTiming(ms, ms));
	}

	[RelayCommand]
	private void ApplyDurationForAllFrames()
	{
		var timing = GetCurrentTiming();
		if (timing == null)
			return;
		ThingFrameGroupEditor.SetDurationForAllFrames(CurrentFrameGroup, timing.Value);
		ApplyToCatalog();
		OnPropertyChanged(nameof(MinimumDuration));
		OnPropertyChanged(nameof(MaximumDuration));
	}

	[RelayCommand]
	private void ToggleAnimationPreview()
	{
		if (IsAnimationPlaying)
			StopAnimationPreview();
		else
			StartAnimationPreview();
	}

	private void StartAnimationPreview()
	{
		if (!ShowFrameSlider)
			return;

		_frameBeforePreview = SelectedFrame;
		_animationDirection = 1;
		IsAnimationPlaying = true;
		ArmAnimationTimer(SelectedFrame);
	}

	private void StopAnimationPreview(bool restoreFrame = true)
	{
		if (_animationTimer != null)
		{
			_animationTimer.Tick -= OnAnimationTimerTick;
			_animationTimer.Stop();
			_animationTimer = null;
		}

		if (!IsAnimationPlaying)
			return;

		IsAnimationPlaying = false;
		if (restoreFrame)
			SelectedFrame = Math.Clamp(_frameBeforePreview, 0, FrameMaximum);
	}

	private void ArmAnimationTimer(int frameIndex)
	{
		_animationTimer?.Stop();
		if (!IsAnimationPlaying)
			return;

		var delayMs = UseCustomPlaySpeed 
			? (double)Math.Max(16, PlaySpeedMs) 
			: (double)Math.Max(16u, GetFrameDelayMs(frameIndex));
		_animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
		_animationTimer.Tick += OnAnimationTimerTick;
		_animationTimer.Start();
	}

	private void OnAnimationTimerTick(object? sender, EventArgs e)
	{
		_animationTimer?.Stop();
		if (!IsAnimationPlaying)
			return;

		var next = ThingAnimationPlayback.GetNextFrame(SelectedFrame, AnimationStartFrame,
			FrameMaximum, IsPingPongStrategy, ref _animationDirection);
		SelectedFrame = next;
		ArmAnimationTimer(next);
	}

	private uint GetFrameDelayMs(int frameIndex)
		=> ThingAnimationPlayback.GetFrameDelayMs(CurrentFrameGroup, frameIndex,
			SettingsViewModel.GetDefaultAnimationDurationMs(Kind), ImprovedAnimations, Kind);

	private void SetOutfitDirection(Direction4 direction)
	{
		_outfitDirection = direction;
		SyncViewPatternsFromDirection();
		RefreshAppearance();
		OnPropertyChanged(nameof(IsDirectionNorth));
		OnPropertyChanged(nameof(IsDirectionEast));
		OnPropertyChanged(nameof(IsDirectionSouth));
		OnPropertyChanged(nameof(IsDirectionWest));
	}

	public bool IsDirectionNorth => _outfitDirection == Direction4.North;
	public bool IsDirectionEast => _outfitDirection == Direction4.East;
	public bool IsDirectionSouth => _outfitDirection == Direction4.South;
	public bool IsDirectionWest => _outfitDirection == Direction4.West;

	private void SyncPatternFieldsFromGroup()
	{
		var group = Thing.GetFrameGroup(SelectedFrameGroupIndex);
		if (group == null)
		{
			OnPropertyChanged(nameof(HasPatterns));
			return;
		}

		if (IsMissile && (group.PatternX < 3 || group.PatternY < 3))
		{
			group.PatternX = Math.Max(3u, group.PatternX);
			group.PatternY = Math.Max(3u, group.PatternY);
			ThingFrameGroupEditor.EnsureSpriteCapacity(group);
		}

		_patternFieldGuard = true;
		_tileWidth = (int)group.Width;
		_tileHeight = (int)group.Height;
		_cropSize = (int)group.ExactSize;
		_layerCount = (int)group.Layers;
		_patternXCount = (int)group.PatternX;
		_patternYCount = (int)group.PatternY;
		_patternZCount = (int)group.PatternZ;
		_frameCount = (int)group.Frames;
		_patternFieldGuard = false;
		NotifyPatternFieldProperties();
		OnPropertyChanged(nameof(HasPatterns));
	}

	private void ApplyPatternChange(Action<ThingFrameGroup> mutate)
	{
		mutate(CurrentFrameGroup);
		ThingFrameGroupEditor.EnsureSpriteCapacity(CurrentFrameGroup);
		_selectedLayer = Math.Clamp(_selectedLayer, 0, LayerMaximum);
		_selectedFrame = Math.Clamp(_selectedFrame, 0, FrameMaximum);
		SyncPatternFieldsFromGroup();
		SyncViewPatternsFromDirection();
		NotifyAppearanceControls();
		NotifySliderDisplays();
		RefreshAppearance();
		ApplyToCatalog();
	}

	private void NotifyPatternFieldProperties()
	{
		OnPropertyChanged(nameof(TileWidth));
		OnPropertyChanged(nameof(TileHeight));
		OnPropertyChanged(nameof(CropSize));
		OnPropertyChanged(nameof(LayerCount));
		OnPropertyChanged(nameof(PatternXCount));
		OnPropertyChanged(nameof(PatternYCount));
		OnPropertyChanged(nameof(PatternZCount));
		OnPropertyChanged(nameof(FrameCount));
	}	private void NotifyThingProperties()
	{
		OnPropertyChanged(nameof(Thing));
		OnPropertyChanged(nameof(ThingId));
		OnPropertyChanged(nameof(Kind));
		OnPropertyChanged(nameof(Title));
		OnPropertyChanged(nameof(ImprovedAnimations));
		OnPropertyChanged(nameof(OutfitFrameGroupsEnabled));
		OnPropertyChanged(nameof(IsOutfit));
		OnPropertyChanged(nameof(IsMissile));
		OnPropertyChanged(nameof(IsItem));
		OnPropertyChanged(nameof(IsEffect));
		OnPropertyChanged(nameof(ShowOutfitDirections));
		OnPropertyChanged(nameof(ShowMissileDirections));
		OnPropertyChanged(nameof(HasPatterns));
		OnPropertyChanged(nameof(IsGround));
		OnPropertyChanged(nameof(GroundSpeed));
		OnPropertyChanged(nameof(HasLight));
		OnPropertyChanged(nameof(LightColor));
		OnPropertyChanged(nameof(LightLevel));
		OnPropertyChanged(nameof(LightColorBrush));
		OnPropertyChanged(nameof(MiniMap));
		OnPropertyChanged(nameof(MiniMapColor));
		OnPropertyChanged(nameof(MiniMapColorBrush));
		OnPropertyChanged(nameof(HasOffset));
		OnPropertyChanged(nameof(OffsetX));
		OnPropertyChanged(nameof(OffsetY));
		OnPropertyChanged(nameof(HasElevation));
		OnPropertyChanged(nameof(Elevation));
		OnPropertyChanged(nameof(IsMarketItem));
		OnPropertyChanged(nameof(MarketName));
		OnPropertyChanged(nameof(MarketCategoryIndex));
		OnPropertyChanged(nameof(MarketTradeAs));
		OnPropertyChanged(nameof(MarketShowAs));
		OnPropertyChanged(nameof(MarketRestrictProfession));
		OnPropertyChanged(nameof(MarketRestrictLevel));
		OnPropertyChanged(nameof(Writable));
		OnPropertyChanged(nameof(WritableOnce));
		OnPropertyChanged(nameof(MaxTextLength));
		OnPropertyChanged(nameof(HasDefaultAction));
		OnPropertyChanged(nameof(DefaultActionIndex));
		OnPropertyChanged(nameof(IsLensHelp));
		OnPropertyChanged(nameof(LensHelpIndex));
		OnPropertyChanged(nameof(IsDat));
		OnPropertyChanged(nameof(IsJson));
		OnPropertyChanged(nameof(ShowGroundBorder));
		OnPropertyChanged(nameof(ShowHasCharges));
		OnPropertyChanged(nameof(ShowNoMoveAnimation));
		OnPropertyChanged(nameof(ShowHangable));
		OnPropertyChanged(nameof(ShowIsVertical));
		OnPropertyChanged(nameof(ShowIsHorizontal));
		OnPropertyChanged(nameof(ShowDontHide));
		OnPropertyChanged(nameof(ShowIsTranslucent));
		OnPropertyChanged(nameof(ShowIgnoreLook));
		OnPropertyChanged(nameof(ShowCloth));
		OnPropertyChanged(nameof(ShowMarket));
		OnPropertyChanged(nameof(ShowHasDefaultAction));
		OnPropertyChanged(nameof(ShowWrappable));
		OnPropertyChanged(nameof(ShowUnwrappable));
		OnPropertyChanged(nameof(ShowBottomEffect));
		OnPropertyChanged(nameof(ShowDontCenterOutfit));
		OnPropertyChanged(nameof(ShowUsable));
		OnPropertyChanged(nameof(ShowFloorChange));

		// Notify remaining flags
		OnPropertyChanged(nameof(IsGroundBorder));
		OnPropertyChanged(nameof(IsOnBottom));
		OnPropertyChanged(nameof(IsOnTop));
		OnPropertyChanged(nameof(IsContainer));
		OnPropertyChanged(nameof(ForceUse));
		OnPropertyChanged(nameof(MultiUse));
		OnPropertyChanged(nameof(HasCharges));
		OnPropertyChanged(nameof(IsFluidContainer));
		OnPropertyChanged(nameof(IsFluid));
		OnPropertyChanged(nameof(IsUnpassable));
		OnPropertyChanged(nameof(IsUnmoveable));
		OnPropertyChanged(nameof(BlockPathfind));
		OnPropertyChanged(nameof(NoMoveAnimation));
		OnPropertyChanged(nameof(Hangable));
		OnPropertyChanged(nameof(IsVertical));
		OnPropertyChanged(nameof(IsHorizontal));
		OnPropertyChanged(nameof(DontHide));
		OnPropertyChanged(nameof(IsTranslucent));
		OnPropertyChanged(nameof(FloorChange));
		OnPropertyChanged(nameof(IsLyingObject));
		OnPropertyChanged(nameof(IsFullGround));
		OnPropertyChanged(nameof(IgnoreLook));
		OnPropertyChanged(nameof(Cloth));
		OnPropertyChanged(nameof(ClothSlot));
		OnPropertyChanged(nameof(Wrappable));
		OnPropertyChanged(nameof(Unwrappable));
		OnPropertyChanged(nameof(Usable));

		NotifyRadioProperties();
		RefreshCustomFlags();
	}

	private AnimationFrameTiming? GetCurrentTiming()
	{
		if (CurrentFrameGroup.FrameTimings == null || SelectedFrame < 0 || SelectedFrame >= CurrentFrameGroup.FrameTimings.Length)
			return null;
		return CurrentFrameGroup.FrameTimings[SelectedFrame];
	}

	private void SetCurrentTiming(AnimationFrameTiming timing)
	{
		if (CurrentFrameGroup.FrameTimings == null)
		{
			var defaults = SettingsViewModel.GetDefaultAnimationDurationMs(Kind);
			ThingFrameGroupEditor.EnsureFrameTimings(CurrentFrameGroup, defaults, defaults);
		}

		CurrentFrameGroup.FrameTimings![SelectedFrame] = timing;
		OnPropertyChanged(nameof(MinimumDuration));
		OnPropertyChanged(nameof(MaximumDuration));
		ApplyToCatalog();
	}

	private void SyncViewPatternsFromDirection()
	{
		if (IsOutfit || IsMissile)
		{
			var (px, py) = ThingAppearanceRenderer.ResolvePatterns(Thing, _outfitDirection, _missileDirection);
			_viewPatternX = px;
			_viewPatternY = py;
		}
		else
		{
			_viewPatternX = (uint)Math.Clamp((int)_viewPatternX, 0, PatternXMaximum);
			_viewPatternY = (uint)Math.Clamp((int)_viewPatternY, 0, PatternYMaximum);
		}

		_viewPatternZ = (uint)Math.Clamp((int)_viewPatternZ, 0, PatternZMaximum);
		OnPropertyChanged(nameof(ViewPatternXIndex));
		OnPropertyChanged(nameof(ViewPatternYIndex));
		OnPropertyChanged(nameof(ViewPatternZIndex));
		NotifySliderDisplays();
	}

	private void NotifySliderDisplays()
	{
		OnPropertyChanged(nameof(LayerDisplay));
		OnPropertyChanged(nameof(FrameDisplay));
		OnPropertyChanged(nameof(PatternXDisplay));
		OnPropertyChanged(nameof(PatternYDisplay));
		OnPropertyChanged(nameof(PatternZDisplay));
	}

	private void SyncAnimationFieldsFromGroup()
	{
		var group = CurrentFrameGroup;
		_selectedAnimationMode = (int)group.AnimationMode;
		_isPingPongStrategy = group.LoopCount < 0;
		_animationLoopCount = group.LoopCount < 0 ? 1 : (int)group.LoopCount;
		_animationStartFrame = group.StartFrame;
		OnPropertyChanged(nameof(SelectedAnimationMode));
		OnPropertyChanged(nameof(IsPingPongStrategy));
		OnPropertyChanged(nameof(AnimationLoopCount));
		OnPropertyChanged(nameof(AnimationStartFrame));
		OnPropertyChanged(nameof(ShowLoopCountEditor));
		OnPropertyChanged(nameof(ShowAnimationSection));
		OnPropertyChanged(nameof(ShowDurationEditors));
		OnPropertyChanged(nameof(MinimumDuration));
		OnPropertyChanged(nameof(MaximumDuration));
		NotifySliderDisplays();
	}

	private void NotifyAppearanceControls()
	{
		OnPropertyChanged(nameof(LayerMaximum));
		OnPropertyChanged(nameof(FrameMaximum));
		OnPropertyChanged(nameof(FrameGroupMaximum));
		OnPropertyChanged(nameof(PatternXMaximum));
		OnPropertyChanged(nameof(PatternYMaximum));
		OnPropertyChanged(nameof(AddonMaximum));
		OnPropertyChanged(nameof(PatternZMaximum));
		OnPropertyChanged(nameof(ShowLayerSlider));
		OnPropertyChanged(nameof(ShowFrameSlider));
		OnPropertyChanged(nameof(ShowPatternXSlider));
		OnPropertyChanged(nameof(ShowPatternYSlider));
		OnPropertyChanged(nameof(ShowPatternZSlider));
		OnPropertyChanged(nameof(ShowAddonSlider));
		OnPropertyChanged(nameof(ShowFrameGroupSlider));
		OnPropertyChanged(nameof(UsesOutfitFrameGroups));
		OnPropertyChanged(nameof(FrameGroupDisplay));
		OnPropertyChanged(nameof(ShowAnimationSection));
		OnPropertyChanged(nameof(ShowDurationEditors));
		OnPropertyChanged(nameof(ShowLoopCountEditor));
	}

	public void RefreshAppearance()
	{
		var loader = SourcePanel.GetActiveSpriteLoader();
		if (loader == null)
		{
			AppearanceImage = null;
			_appearancePixelWidth = 0;
			_appearancePixelHeight = 0;
			OnPropertyChanged(nameof(AppearancePixelWidth));
			OnPropertyChanged(nameof(AppearancePixelHeight));
			return;
		}

		var options = BuildAppearanceOptions();

		var fg = CurrentFrameGroup;
		var edge = SpritePixelCodec.SpriteEdgeLength;
		byte[]? rgba;
		int w;
		int h;

		if (IsMissile)
		{
			rgba = ThingAppearanceRenderer.RenderMissileDirectionGrid(Thing, loader, options);
			w = (int)(fg.Width * edge) * 3;
			h = (int)(fg.Height * edge) * 3;
		}
		else if (IsOutfit && ShowAllOutfitDirections)
		{
			rgba = ThingAppearanceRenderer.RenderOutfitDirectionGrid(Thing, loader, options);
			w = (int)(fg.Width * edge) * 4;
			h = (int)(fg.Height * edge);
		}
		else if (ShowPatternGrid)
		{
			rgba = ThingAppearanceRenderer.RenderPatternGrid(Thing, loader, options);
			w = (int)(fg.PatternX * fg.Width * edge);
			h = (int)(fg.PatternY * fg.Height * edge);
		}
		else
		{
			rgba = ThingAppearanceRenderer.Render(Thing, loader, options);
			w = (int)(fg.Width * edge);
			h = (int)(fg.Height * edge);
		}

		if (rgba == null && _isAppearanceDragHover && w > 0 && h > 0)
			rgba = ThingAppearanceRenderer.RenderDragPreviewOverlay(w, h, fg, options, IsMissile, ShowPatternGrid);

		if (rgba == null)
		{
			AppearanceImage = null;
			_appearancePixelWidth = 0;
			_appearancePixelHeight = 0;
			OnPropertyChanged(nameof(AppearancePixelWidth));
			OnPropertyChanged(nameof(AppearancePixelHeight));
			return;
		}

		_appearancePixelWidth = w;
		_appearancePixelHeight = h;
		OnPropertyChanged(nameof(AppearancePixelWidth));
		OnPropertyChanged(nameof(AppearancePixelHeight));
		OnPropertyChanged(nameof(CanGenerateMissileOrthogonalDirections));
		OnPropertyChanged(nameof(CanGenerateMissileDiagonalDirections));
		GenerateMissileOrthogonalDirectionsCommand.NotifyCanExecuteChanged();
		GenerateMissileDiagonalDirectionsCommand.NotifyCanExecuteChanged();
		AppearanceImage = _renderer.ConvertRgba(w, h, rgba);
	}

	private ThingAppearanceOptions BuildAppearanceOptions()
	{
		(int X, int Y, int Width, int Height)? highlightRect = null;
		if (_isAppearanceDragHover && _hoverSlot is { } slot)
			highlightRect = ThingAppearanceSlotGeometry.GetHighlightRect(this, slot);
		else if (_selectedSlot is { } selSlot)
			highlightRect = ThingAppearanceSlotGeometry.GetHighlightRect(this, selSlot);

		return new ThingAppearanceOptions
		{
			FrameGroupIndex = SelectedFrameGroupIndex,
			Layer = SelectedLayer,
			Frame = SelectedFrame,
			PatternX = _viewPatternX,
			PatternY = _viewPatternY,
			PatternZ = _viewPatternZ,
			ShowGrid = ShowGrid,
			ShowDragGrid = _isAppearanceDragHover,
			ShowCropSize = ShowCropSize,
			HighlightRect = highlightRect,
			GridColor = AppearanceGridColorParser.Parse(SettingsViewModel.ThingEditorGridColor, new SkiaSharp.SKColor(80, 80, 80, 180)),
			GridLineWidth = SettingsViewModel.ThingEditorGridLineWidth,
			DragGridColor = AppearanceGridColorParser.Parse(SettingsViewModel.ThingEditorDragGridColor, new SkiaSharp.SKColor(255, 105, 180, 180)),
			DragGridLineWidth = SettingsViewModel.ThingEditorDragGridLineWidth,
			HighlightColor = AppearanceGridColorParser.Parse(SettingsViewModel.ThingEditorDragHighlightColor, new SkiaSharp.SKColor(58, 123, 213, 128)),
		};
	}

	private void ApplyToCatalog([CallerMemberName] string? propertyName = null)
	{
		if (IsBatchEditor && !string.IsNullOrEmpty(propertyName)) BatchTouchedProperties.Add(propertyName);
		IsDirty = true;
	}

	[RelayCommand]
	public void Save()
	{
		if (!IsDirty) return;
		if (BatchSaveRequested != null)
		{
			BatchSaveRequested(Thing);
			_originalThing = Services.Exchange.ThingCloner.Clone(Thing, Thing.Id);
			IsDirty = false;
			Dispatcher.UIThread.Post(() =>
			{
				BatchTouchedProperties.Clear();
				IsDirty = false;
			}, DispatcherPriority.Background);
			return;
		}
		SourcePanel.ApplyThingEdit(Thing);
		_originalThing = Services.Exchange.ThingCloner.Clone(Thing, Thing.Id);
		IsDirty = false;
		SourcePanel.HasSavedChanges = true;
	}

	[RelayCommand]
	public void Cancel()
	{
		if (BatchCancelRequested != null)
		{
			BatchCancelRequested();
			return;
		}
		LoadThing(_originalThing);
	}

	public int FrameStrategyIndex
	{
		get => IsPingPongStrategy ? 1 : 0;
		set => IsPingPongStrategy = value == 1;
	}

	public bool AnimateAlways
	{
		get => Thing.AnimateAlways;
		set { if (Thing.AnimateAlways == value) return; Thing.AnimateAlways = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowDurationEditors)); ApplyToCatalog(); }
	}

	public bool BottomEffect
	{
		get => Thing.BottomEffect;
		set { if (Thing.BottomEffect == value) return; Thing.BottomEffect = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool DontCenterOutfit
	{
		get => Thing.DontCenterOutfit;
		set { if (Thing.DontCenterOutfit == value) return; Thing.DontCenterOutfit = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool Stackable
	{
		get => Thing.Stackable;
		set { if (Thing.Stackable == value) return; Thing.Stackable = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool Rotatable
	{
		get => Thing.Rotatable;
		set { if (Thing.Rotatable == value) return; Thing.Rotatable = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool Pickupable
	{
		get => Thing.Pickupable;
		set { if (Thing.Pickupable == value) return; Thing.Pickupable = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool BlockMissile
	{
		get => Thing.BlockMissile;
		set { if (Thing.BlockMissile == value) return; Thing.BlockMissile = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool IsGround
	{
		get => Thing.IsGround;
		set { if (Thing.IsGround == value) return; Thing.IsGround = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public uint GroundSpeed
	{
		get => Thing.GroundSpeed;
		set { if (Thing.GroundSpeed == value) return; Thing.GroundSpeed = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool HasLight
	{
		get => Thing.HasLight;
		set { if (Thing.HasLight == value) return; Thing.HasLight = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public uint LightColor
	{
		get => Thing.LightColor;
		set
		{
			if (Thing.LightColor == value) return;
			Thing.LightColor = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(LightColorBrush));
			ApplyToCatalog();
		}
	}

	public uint LightLevel
	{
		get => Thing.LightLevel;
		set { if (Thing.LightLevel == value) return; Thing.LightLevel = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool MiniMap
	{
		get => Thing.MiniMap;
		set { if (Thing.MiniMap == value) return; Thing.MiniMap = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public uint MiniMapColor
	{
		get => Thing.MiniMapColor;
		set
		{
			if (Thing.MiniMapColor == value) return;
			Thing.MiniMapColor = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(MiniMapColorBrush));
			ApplyToCatalog();
		}
	}

	public bool HasOffset
	{
		get => Thing.HasOffset;
		set { if (Thing.HasOffset == value) return; Thing.HasOffset = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public int OffsetX
	{
		get => Thing.OffsetX;
		set { if (Thing.OffsetX == value) return; Thing.OffsetX = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public int OffsetY
	{
		get => Thing.OffsetY;
		set { if (Thing.OffsetY == value) return; Thing.OffsetY = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool HasElevation
	{
		get => Thing.HasElevation;
		set { if (Thing.HasElevation == value) return; Thing.HasElevation = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public uint Elevation
	{
		get => Thing.Elevation;
		set { if (Thing.Elevation == value) return; Thing.Elevation = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool IsMarketItem
	{
		get => Thing.IsMarketItem;
		set { if (Thing.IsMarketItem == value) return; Thing.IsMarketItem = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public string MarketName
	{
		get => Thing.MarketName ?? string.Empty;
		set { if (Thing.MarketName == value) return; Thing.MarketName = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public uint MarketTradeAs
	{
		get => Thing.MarketTradeAs;
		set { if (Thing.MarketTradeAs == value) return; Thing.MarketTradeAs = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public uint MarketShowAs
	{
		get => Thing.MarketShowAs;
		set { if (Thing.MarketShowAs == value) return; Thing.MarketShowAs = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public uint MarketRestrictProfession
	{
		get => Thing.MarketRestrictProfession;
		set { if (Thing.MarketRestrictProfession == value) return; Thing.MarketRestrictProfession = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public uint MarketRestrictLevel
	{
		get => Thing.MarketRestrictLevel;
		set { if (Thing.MarketRestrictLevel == value) return; Thing.MarketRestrictLevel = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool Writable
	{
		get => Thing.Writable;
		set { if (Thing.Writable == value) return; Thing.Writable = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool WritableOnce
	{
		get => Thing.WritableOnce;
		set { if (Thing.WritableOnce == value) return; Thing.WritableOnce = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public uint MaxTextLength
	{
		get => Thing.MaxTextLength;
		set { if (Thing.MaxTextLength == value) return; Thing.MaxTextLength = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool HasDefaultAction
	{
		get => Thing.HasDefaultAction;
		set { if (Thing.HasDefaultAction == value) return; Thing.HasDefaultAction = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool IsLensHelp
	{
		get => Thing.IsLensHelp;
		set { if (Thing.IsLensHelp == value) return; Thing.IsLensHelp = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public int MarketCategoryIndex
	{
		get
		{
			int val = (int)Thing.MarketCategory;
			if (val <= 0) return 8; // "Others"
			return val - 1;
		}
		set
		{
			int val = value + 1;
			if (Thing.MarketCategory == (uint)val) return;
			Thing.MarketCategory = (uint)val;
			OnPropertyChanged();
			ApplyToCatalog();
		}
	}

	public int LensHelpIndex
	{
		get
		{
			int val = (int)Thing.LensHelp;
			return Math.Max(0, val - 1100);
		}
		set
		{
			int val = value + 1100;
			if (Thing.LensHelp == (uint)val) return;
			Thing.LensHelp = (uint)val;
			OnPropertyChanged();
			ApplyToCatalog();
		}
	}

	public int DefaultActionIndex
	{
		get => (int)Thing.DefaultAction;
		set
		{
			if (Thing.DefaultAction == (uint)value) return;
			Thing.DefaultAction = (uint)value;
			OnPropertyChanged();
			ApplyToCatalog();
		}
	}

	public static System.Collections.Generic.List<string> MarketCategories { get; } = new()
	{
		"Armors", "Amulets", "Boots", "Containers", "Decoration", "Foods",
		"Helmets and Hats", "Legs", "Others", "Potions", "Rings", "Runes",
		"Shields", "Tools", "Valuables", "Ammunition", "Axes", "Clubs",
		"Distance", "Swords", "Wands and Rods", "Premium Scrolls", "Meta Weapons"
	};

	public static System.Collections.Generic.List<string> DefaultActions { get; } = new()
	{
		"None", "Look", "Use", "Open", "Autowalk Highlight"
	};

	public static System.Collections.Generic.List<string> LensHelpTypes { get; } = new()
	{
		"Ladders", "Sewer Grates", "Dungeon Floor", "Levers", "Doors",
		"Special Doors", "Stairs", "Mailboxes", "Depot Boxes", "Dustbins",
		"Stone Piles", "Signs", "Books and Scrolls"
	};

	public Avalonia.Media.IBrush LightColorBrush => new Avalonia.Media.SolidColorBrush(Get8BitColor((int)LightColor));
	public Avalonia.Media.IBrush MiniMapColorBrush => new Avalonia.Media.SolidColorBrush(Get8BitColor((int)MiniMapColor));

	public class PaletteColor
	{
		public int Index { get; }
		public string Hex { get; }
		public PaletteColor(int index, string hex)
		{
			Index = index;
			Hex = hex;
		}
	}

	private static readonly System.Collections.Generic.List<PaletteColor> _paletteColors = GeneratePaletteColors();
	public System.Collections.Generic.List<PaletteColor> PaletteColors => _paletteColors;
	public static System.Collections.Generic.IReadOnlyList<PaletteColor> SharedPaletteColors => _paletteColors;

	private static System.Collections.Generic.List<PaletteColor> GeneratePaletteColors()
	{
		var list = new System.Collections.Generic.List<PaletteColor>();
		for (int i = 0; i < 224; i++)
		{
			var c = Get8BitColor(i);
			var hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
			list.Add(new PaletteColor(i, hex));
		}
		return list;
	}

	public static Avalonia.Media.Color Get8BitColor(int index)
	{
		if (index < 0 || index >= 224) return Avalonia.Media.Colors.Black;
		if (index >= 216) return Avalonia.Media.Colors.Black;
		int r = (index / 36) % 6 * 51;
		int g = (index / 6) % 6 * 51;
		int b = index % 6 * 51;
		return Avalonia.Media.Color.FromRgb((byte)r, (byte)g, (byte)b);
	}

	[RelayCommand]
	public void SelectLightColor(int colorIndex)
	{
		LightColor = (uint)colorIndex;
	}

	[RelayCommand]
	public void SelectMiniMapColor(int colorIndex)
	{
		MiniMapColor = (uint)colorIndex;
	}

	public bool IsFlagsCommon
	{
		get => !Thing.IsGroundBorder && !Thing.IsOnBottom && !Thing.IsOnTop;
		set
		{
			if (value)
			{
				Thing.IsGroundBorder = false;
				Thing.IsOnBottom = false;
				Thing.IsOnTop = false;
				NotifyRadioProperties();
				ApplyToCatalog();
			}
		}
	}

	public bool IsFlagsGroundBorder
	{
		get => Thing.IsGroundBorder;
		set
		{
			if (value)
			{
				Thing.IsGroundBorder = true;
				Thing.IsOnBottom = false;
				Thing.IsOnTop = false;
				NotifyRadioProperties();
				ApplyToCatalog();
			}
		}
	}

	public bool IsFlagsBottom
	{
		get => Thing.IsOnBottom;
		set
		{
			if (value)
			{
				Thing.IsGroundBorder = false;
				Thing.IsOnBottom = true;
				Thing.IsOnTop = false;
				NotifyRadioProperties();
				ApplyToCatalog();
			}
		}
	}

	public bool IsFlagsTop
	{
		get => Thing.IsOnTop;
		set
		{
			if (value)
			{
				Thing.IsGroundBorder = false;
				Thing.IsOnBottom = false;
				Thing.IsOnTop = true;
				NotifyRadioProperties();
				ApplyToCatalog();
			}
		}
	}

	public void NotifyRadioProperties()
	{
		OnPropertyChanged(nameof(IsFlagsCommon));
		OnPropertyChanged(nameof(IsFlagsGroundBorder));
		OnPropertyChanged(nameof(IsFlagsBottom));
		OnPropertyChanged(nameof(IsFlagsTop));
	}

	public enum DatVersionFormat
	{
		V1,
		V2,
		V3,
		V4,
		V5,
		V6
	}

	public DatVersionFormat DatVersion
	{
		get
		{
			uint v = SettingsViewModel.ClientVersion;
			if (v < 740) return DatVersionFormat.V1;
			if (v < 755) return DatVersionFormat.V2;
			if (v < 780) return DatVersionFormat.V3;
			if (v < 860) return DatVersionFormat.V4;
			if (v < 1010) return DatVersionFormat.V5;
			return DatVersionFormat.V6;
		}
	}

	public bool IsDat => SourcePanel.ArchiveFormat == Common.ArchiveFormat.Dat;
	public bool IsJson => SourcePanel.ArchiveFormat == Common.ArchiveFormat.Things;

	public bool IsGroundBorder
	{
		get => Thing.IsGroundBorder;
		set { if (Thing.IsGroundBorder == value) return; Thing.IsGroundBorder = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool IsOnBottom
	{
		get => Thing.IsOnBottom;
		set { if (Thing.IsOnBottom == value) return; Thing.IsOnBottom = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool IsOnTop
	{
		get => Thing.IsOnTop;
		set { if (Thing.IsOnTop == value) return; Thing.IsOnTop = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool IsContainer
	{
		get => Thing.IsContainer;
		set { if (Thing.IsContainer == value) return; Thing.IsContainer = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool ForceUse
	{
		get => Thing.ForceUse;
		set { if (Thing.ForceUse == value) return; Thing.ForceUse = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool MultiUse
	{
		get => Thing.MultiUse;
		set { if (Thing.MultiUse == value) return; Thing.MultiUse = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool HasCharges
	{
		get => Thing.HasCharges;
		set { if (Thing.HasCharges == value) return; Thing.HasCharges = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool IsFluidContainer
	{
		get => Thing.IsFluidContainer;
		set { if (Thing.IsFluidContainer == value) return; Thing.IsFluidContainer = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool IsFluid
	{
		get => Thing.IsFluid;
		set { if (Thing.IsFluid == value) return; Thing.IsFluid = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool IsUnpassable
	{
		get => Thing.IsUnpassable;
		set { if (Thing.IsUnpassable == value) return; Thing.IsUnpassable = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool IsUnmoveable
	{
		get => Thing.IsUnmoveable;
		set { if (Thing.IsUnmoveable == value) return; Thing.IsUnmoveable = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool BlockPathfind
	{
		get => Thing.BlockPathfind;
		set { if (Thing.BlockPathfind == value) return; Thing.BlockPathfind = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool NoMoveAnimation
	{
		get => Thing.NoMoveAnimation;
		set { if (Thing.NoMoveAnimation == value) return; Thing.NoMoveAnimation = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool Hangable
	{
		get => Thing.Hangable;
		set { if (Thing.Hangable == value) return; Thing.Hangable = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool IsVertical
	{
		get => Thing.IsVertical;
		set { if (Thing.IsVertical == value) return; Thing.IsVertical = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool IsHorizontal
	{
		get => Thing.IsHorizontal;
		set { if (Thing.IsHorizontal == value) return; Thing.IsHorizontal = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool DontHide
	{
		get => Thing.DontHide;
		set { if (Thing.DontHide == value) return; Thing.DontHide = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool IsTranslucent
	{
		get => Thing.IsTranslucent;
		set { if (Thing.IsTranslucent == value) return; Thing.IsTranslucent = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool FloorChange
	{
		get => Thing.FloorChange;
		set { if (Thing.FloorChange == value) return; Thing.FloorChange = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool IsLyingObject
	{
		get => Thing.IsLyingObject;
		set { if (Thing.IsLyingObject == value) return; Thing.IsLyingObject = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool IsFullGround
	{
		get => Thing.IsFullGround;
		set { if (Thing.IsFullGround == value) return; Thing.IsFullGround = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool IgnoreLook
	{
		get => Thing.IgnoreLook;
		set { if (Thing.IgnoreLook == value) return; Thing.IgnoreLook = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool Cloth
	{
		get => Thing.Cloth;
		set { if (Thing.Cloth == value) return; Thing.Cloth = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public uint ClothSlot
	{
		get => Thing.ClothSlot;
		set { if (Thing.ClothSlot == value) return; Thing.ClothSlot = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool Wrappable
	{
		get => Thing.Wrappable;
		set { if (Thing.Wrappable == value) return; Thing.Wrappable = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool Unwrappable
	{
		get => Thing.Unwrappable;
		set { if (Thing.Unwrappable == value) return; Thing.Unwrappable = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool Usable
	{
		get => Thing.Usable;
		set { if (Thing.Usable == value) return; Thing.Usable = value; OnPropertyChanged(); ApplyToCatalog(); }
	}

	public bool ShowGroundBorder => DatVersion >= DatVersionFormat.V3;
	public bool ShowHasCharges => DatVersion == DatVersionFormat.V4;
	public bool ShowNoMoveAnimation => DatVersion >= DatVersionFormat.V6;
	public bool ShowHangable => DatVersion >= DatVersionFormat.V2;
	public bool ShowIsVertical => DatVersion >= DatVersionFormat.V2;
	public bool ShowIsHorizontal => DatVersion >= DatVersionFormat.V2;
	public bool ShowDontHide => DatVersion >= DatVersionFormat.V4;
	public bool ShowIsTranslucent => DatVersion >= DatVersionFormat.V5;
	public bool ShowIgnoreLook => DatVersion >= DatVersionFormat.V4;
	public bool ShowCloth => DatVersion >= DatVersionFormat.V5;
	public bool ShowMarket => SettingsViewModel.ClientVersion >= 940 && IsItem;
	public bool ShowHasDefaultAction => DatVersion >= DatVersionFormat.V6 && IsItem;
	public bool ShowWrappable => DatVersion == DatVersionFormat.V1 || DatVersion == DatVersionFormat.V2 || DatVersion >= DatVersionFormat.V6;
	public bool ShowUnwrappable => DatVersion == DatVersionFormat.V1 || DatVersion == DatVersionFormat.V2 || DatVersion >= DatVersionFormat.V6;
	public bool ShowFloorChange => DatVersion <= DatVersionFormat.V4;
	public bool ShowBottomEffect => DatVersion >= DatVersionFormat.V6;
	public bool ShowDontCenterOutfit => DatVersion >= DatVersionFormat.V5;
	public bool ShowUsable => DatVersion >= DatVersionFormat.V6;

	public ObservableCollection<FlagGroupViewModel> CustomFlagGroups { get; } = new();
	public ObservableCollection<AdHocFlagViewModel> AdHocFlags { get; } = new();
	private readonly HashSet<string> _adHocFlagNames = new(StringComparer.Ordinal);
	private CustomFlagSchema _customSchema = new();
	private string _newFlagName = string.Empty;

	public string NewFlagName
	{
		get => _newFlagName;
		set => SetProperty(ref _newFlagName, value);
	}

	// Special Sub-Window Modal States & Flags
	public ObservableCollection<CustomFlagViewModelBase> SkillsFlags { get; } = new();
	public ObservableCollection<CustomFlagViewModelBase> ElementsFlags { get; } = new();
	public ObservableCollection<CustomFlagViewModelBase> AbsorbsFlags { get; } = new();
	public ObservableCollection<CustomFlagViewModelBase> LeechFlags { get; } = new();
	public ObservableCollection<CustomFlagViewModelBase> HealthManaFlags { get; } = new();
	public ObservableCollection<CustomFlagViewModelBase> SuppressionsFlags { get; } = new();
	public ObservableCollection<CustomFlagViewModelBase> FieldFlags { get; } = new();

	private bool _showSkillsModal;
	public bool ShowSkillsModal
	{
		get => _showSkillsModal;
		set => SetProperty(ref _showSkillsModal, value);
	}

	private bool _showElementsModal;
	public bool ShowElementsModal
	{
		get => _showElementsModal;
		set => SetProperty(ref _showElementsModal, value);
	}

	private bool _showAbsorbsModal;
	public bool ShowAbsorbsModal
	{
		get => _showAbsorbsModal;
		set => SetProperty(ref _showAbsorbsModal, value);
	}

	private bool _showLeechModal;
	public bool ShowLeechModal
	{
		get => _showLeechModal;
		set => SetProperty(ref _showLeechModal, value);
	}

	private bool _showHealthManaModal;
	public bool ShowHealthManaModal
	{
		get => _showHealthManaModal;
		set => SetProperty(ref _showHealthManaModal, value);
	}

	private bool _showSuppressionsModal;
	public bool ShowSuppressionsModal
	{
		get => _showSuppressionsModal;
		set => SetProperty(ref _showSuppressionsModal, value);
	}

	private CustomFlagViewModelBase? _activeChildFlag;
	public CustomFlagViewModelBase? ActiveChildFlag
	{
		get => _activeChildFlag;
		set
		{
			if (SetProperty(ref _activeChildFlag, value))
				OnPropertyChanged(nameof(ShowChildModal));
		}
	}

	public bool ShowChildModal => ActiveChildFlag != null;

	[RelayCommand]
	public void OpenChildModal(CustomFlagViewModelBase flag) => ActiveChildFlag = flag;
	public void CloseChildModal() => ActiveChildFlag = null;

	[RelayCommand] public void CloseChildModalCommand() => CloseChildModal();

	[RelayCommand] public void OpenSkillsModal() => ShowSkillsModal = true;
	[RelayCommand] public void CloseSkillsModal() => ShowSkillsModal = false;

	[RelayCommand] public void OpenElementsModal() => ShowElementsModal = true;
	[RelayCommand] public void CloseElementsModal() => ShowElementsModal = false;

	[RelayCommand] public void OpenAbsorbsModal() => ShowAbsorbsModal = true;
	[RelayCommand] public void CloseAbsorbsModal() => ShowAbsorbsModal = false;

	[RelayCommand] public void OpenLeechModal() => ShowLeechModal = true;
	[RelayCommand] public void CloseLeechModal() => ShowLeechModal = false;

	[RelayCommand] public void OpenHealthManaModal() => ShowHealthManaModal = true;
	[RelayCommand] public void CloseHealthManaModal() => ShowHealthManaModal = false;

	[RelayCommand] public void OpenSuppressionsModal() => ShowSuppressionsModal = true;
	[RelayCommand] public void CloseSuppressionsModal() => ShowSuppressionsModal = false;

	private bool _showFieldModal;
	public bool ShowFieldModal
	{
		get => _showFieldModal;
		set => SetProperty(ref _showFieldModal, value);
	}

	[RelayCommand] public void OpenFieldModal() => ShowFieldModal = true;
	[RelayCommand] public void CloseFieldModal() => ShowFieldModal = false;

	// Flag Creator Modal State
	private bool _showFlagCreatorModal;
	public bool ShowFlagCreatorModal
	{
		get => _showFlagCreatorModal;
		set => SetProperty(ref _showFlagCreatorModal, value);
	}

	private string _creatorKey = string.Empty;
	public string CreatorKey
	{
		get => _creatorKey;
		set => SetProperty(ref _creatorKey, value);
	}

	private string _creatorLabel = string.Empty;
	public string CreatorLabel
	{
		get => _creatorLabel;
		set => SetProperty(ref _creatorLabel, value);
	}

	private int _creatorTypeIndex;
	public int CreatorTypeIndex
	{
		get => _creatorTypeIndex;
		set
		{
			if (SetProperty(ref _creatorTypeIndex, value))
			{
				OnPropertyChanged(nameof(IsCreatorTypeInt));
				OnPropertyChanged(nameof(IsCreatorTypeEnum));
			}
		}
	}

	public bool IsCreatorTypeInt => CreatorTypeIndex == 1;
	public bool IsCreatorTypeEnum => CreatorTypeIndex == 3 || CreatorTypeIndex == 4;

	private string _creatorGroup = "Custom Flags";
	public string CreatorGroup
	{
		get => _creatorGroup;
		set => SetProperty(ref _creatorGroup, value);
	}

	private string _creatorDefault = string.Empty;
	public string CreatorDefault
	{
		get => _creatorDefault;
		set => SetProperty(ref _creatorDefault, value);
	}

	private int _creatorMin;
	public int CreatorMin
	{
		get => _creatorMin;
		set => SetProperty(ref _creatorMin, value);
	}

	private int _creatorMax = 100;
	public int CreatorMax
	{
		get => _creatorMax;
		set => SetProperty(ref _creatorMax, value);
	}

	private string _creatorOptionsRaw = string.Empty;
	public string CreatorOptionsRaw
	{
		get => _creatorOptionsRaw;
		set => SetProperty(ref _creatorOptionsRaw, value);
	}

	[RelayCommand]
	public void OpenFlagCreator()
	{
		CreatorKey = string.Empty;
		CreatorLabel = string.Empty;
		CreatorTypeIndex = 0;
		CreatorGroup = "Custom Flags";
		CreatorDefault = string.Empty;
		CreatorMin = 0;
		CreatorMax = 100;
		CreatorOptionsRaw = string.Empty;
		ShowFlagCreatorModal = true;
	}

	[RelayCommand]
	public void CloseFlagCreator()
	{
		ShowFlagCreatorModal = false;
	}

	[RelayCommand]
	public void SaveFlagDefinition()
	{
		if (string.IsNullOrWhiteSpace(CreatorKey)) return;
		string key = CreatorKey.Trim();

		string flagType = CreatorTypeIndex switch
		{
			1 => "int",
			2 => "string",
			3 => "enum",
			4 => "enum",
			_ => "bool",
		};

		string groupType = CreatorTypeIndex == 4 ? "radio" : "dropdown";
		string groupKey = CreatorGroup.Trim().ToLowerInvariant().Replace(' ', '_');

		var optionsList = IsCreatorTypeEnum
			? CreatorOptionsRaw.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
			: null;

		var def = new CustomFlagDefinition
		{
			Name = key,
			Label = string.IsNullOrWhiteSpace(CreatorLabel) ? key : CreatorLabel.Trim(),
			Type = flagType,
			Group = groupKey,
			GroupType = groupType,
			Locked = CreatorLocked,
			Default = string.IsNullOrWhiteSpace(CreatorDefault) ? null : CreatorDefault.Trim(),
			Min = IsCreatorTypeInt ? CreatorMin : null,
			Max = IsCreatorTypeInt ? CreatorMax : null,
			Options = optionsList,
		};

		string? archivePath = SourcePanel.FilePath;
		if (archivePath == "No things loaded") archivePath = null;
		string versionDirName = DatVersion.ToString().ToLowerInvariant();

		CustomFlagSchemaLoader.SaveDefinition(archivePath, versionDirName, def, CreatorGroup.Trim());

		// Assign default value if set
		if (!string.IsNullOrEmpty(def.Default))
		{
			Thing.ExtraProperties[key] = def.Default;
			ApplyToCatalog();
		}

		ShowFlagCreatorModal = false;
		LoadCustomFlagSchema();
		RefreshCustomFlags();
	}

	private bool _creatorLocked;
	public bool CreatorLocked
	{
		get => _creatorLocked;
		set => SetProperty(ref _creatorLocked, value);
	}

	private static readonly Dictionary<string, System.Reflection.PropertyInfo> PropertyMap =
		typeof(ThingType).GetProperties()
			.ToDictionary(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..], p => p, StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, int> _cachedFlagUsageCounts = new(StringComparer.OrdinalIgnoreCase);

	public int GetFlagUsageCount(string flagName)
	{
		if (SourcePanel.GetOrBuildFlagUsageCounts().TryGetValue(flagName, out var count))
			return Math.Max(count, 1);
		return 1;
	}

	public void RemoveSchemaFlag(string flagName)
	{
		Thing.ExtraProperties.Remove(flagName);
		if (PropertyMap.TryGetValue(flagName, out var prop) && prop.CanWrite)
		{
			if (prop.PropertyType == typeof(bool)) prop.SetValue(Thing, false);
			else if (prop.PropertyType == typeof(uint)) prop.SetValue(Thing, 0u);
			else if (prop.PropertyType == typeof(int)) prop.SetValue(Thing, 0);
			else if (prop.PropertyType == typeof(string)) prop.SetValue(Thing, null);
		}
		ApplyToCatalog();
		RefreshCustomFlags();
	}

	private int _newFlagTypeIndex;
	public int NewFlagTypeIndex
	{
		get => _newFlagTypeIndex;
		set => SetProperty(ref _newFlagTypeIndex, value);
	}

	[RelayCommand]
	public void AddAdHocFlag()
	{
		if (string.IsNullOrWhiteSpace(NewFlagName)) return;
		string name = NewFlagName.Trim();
		if (_customSchema.Flags.Any(f => f.Name == name)) return;
		if (!_adHocFlagNames.Contains(name))
		{
			_adHocFlagNames.Add(name);
			AdHocFlags.Add(new AdHocFlagViewModel(name, this, NewFlagTypeIndex));
		}
		string defaultValue = NewFlagTypeIndex switch
		{
			1 => "0",
			2 => "",
			_ => "true",
		};
		if (!string.IsNullOrEmpty(defaultValue))
			Thing.ExtraProperties[name] = defaultValue;
		NewFlagName = string.Empty;
		ApplyToCatalog();
	}

	public void RemoveAdHocFlag(string flagName)
	{
		_adHocFlagNames.Remove(flagName);
		var vm = AdHocFlags.FirstOrDefault(f => f.Name == flagName);
		if (vm != null) AdHocFlags.Remove(vm);
		Thing.ExtraProperties.Remove(flagName);
		ApplyToCatalog();
	}

	private void LoadCustomFlagSchema()
	{
		if (!IsJson) return;

		string? archivePath = SourcePanel.FilePath;
		if (archivePath == "No things loaded") archivePath = null;
		string versionDirName = DatVersion.ToString().ToLowerInvariant();

		_customSchema = CustomFlagSchemaLoader.Load(archivePath, versionDirName);
	}

	public void RefreshCustomFlags()
	{
		CustomFlagGroups.Clear();
		AdHocFlags.Clear();
		_adHocFlagNames.Clear();
		_cachedFlagUsageCounts.Clear();

		if (!IsJson) return;

		var usageCounts = SourcePanel.GetOrBuildFlagUsageCounts();

		// Build schema-defined flag groups
		var schemaKeys = new HashSet<string>(StringComparer.Ordinal);
		var groupMap = new Dictionary<string, FlagGroupViewModel>(StringComparer.Ordinal);
		var flagVmMap = new Dictionary<string, CustomFlagViewModelBase>(StringComparer.OrdinalIgnoreCase);

		foreach (var def in _customSchema.Flags)
		{
			schemaKeys.Add(def.Name);

			CustomFlagViewModelBase flagVm = def.Type.ToLowerInvariant() switch
			{
				"int" => new IntFlagViewModel(def, this),
				"string" => new StringFlagViewModel(def, this),
				"enum" => new EnumFlagViewModel(def, this),
				_ => new BoolFlagViewModel(def, this),
			};
			flagVmMap[def.Name] = flagVm;

			if (!string.IsNullOrEmpty(def.Parent))
				continue; // Will be attached as child flag

			string groupKey = def.Group ?? "_default";
			if (!groupMap.TryGetValue(groupKey, out var groupVm))
			{
				string groupLabel;
				int groupOrder;
				if (_customSchema.Groups.TryGetValue(groupKey, out var gDef))
				{
					groupLabel = gDef.Label;
					groupOrder = gDef.Order;
				}
				else
				{
					groupLabel = groupKey == "_default" ? "Custom Flags" : groupKey;
					groupOrder = groupKey == "_default" ? 999 : 500;
				}
				groupVm = new FlagGroupViewModel(groupKey, groupLabel, groupOrder);
				groupMap[groupKey] = groupVm;
			}

			groupVm.Flags.Add(flagVm);
		}

		// Attach child flags to parent flag ViewModels
		foreach (var def in _customSchema.Flags)
		{
			if (!string.IsNullOrEmpty(def.Parent) && flagVmMap.TryGetValue(def.Parent, out var parentVm) && flagVmMap.TryGetValue(def.Name, out var childVm))
			{
				parentVm.ChildFlags.Add(childVm);
			}
		}

		SkillsFlags.Clear();
		ElementsFlags.Clear();
		AbsorbsFlags.Clear();
		LeechFlags.Clear();
		HealthManaFlags.Clear();
		SuppressionsFlags.Clear();
		FieldFlags.Clear();

		foreach (var g in groupMap.Values.OrderBy(g => g.Order).ThenBy(g => g.Label))
		{
			if (g.GroupKey.Equals("skills_boost", StringComparison.OrdinalIgnoreCase))
			{
				foreach (var f in g.Flags) SkillsFlags.Add(f);
			}
			else if (g.GroupKey.Equals("elements_damage", StringComparison.OrdinalIgnoreCase))
			{
				foreach (var f in g.Flags) ElementsFlags.Add(f);
			}
			else if (g.GroupKey.Equals("absorbs_protection", StringComparison.OrdinalIgnoreCase))
			{
				foreach (var f in g.Flags) AbsorbsFlags.Add(f);
			}
			else if (g.GroupKey.Equals("special_leech", StringComparison.OrdinalIgnoreCase))
			{
				foreach (var f in g.Flags) LeechFlags.Add(f);
			}
			else if (g.GroupKey.Equals("health_mana", StringComparison.OrdinalIgnoreCase))
			{
				foreach (var f in g.Flags) HealthManaFlags.Add(f);
			}
			else if (g.GroupKey.Equals("suppressions_condition", StringComparison.OrdinalIgnoreCase))
			{
				foreach (var f in g.Flags) SuppressionsFlags.Add(f);
			}
			else if (g.GroupKey.Equals("field_properties", StringComparison.OrdinalIgnoreCase))
			{
				foreach (var f in g.Flags) FieldFlags.Add(f);
			}
			else
			{
				CustomFlagGroups.Add(g);
			}
		}

		// Build ad-hoc flags: ExtraProperties keys not covered by schema
		if (SourcePanel.Catalog != null)
		{
			foreach (var key in usageCounts.Keys)
			{
				if (!schemaKeys.Contains(key) && !PropertyMap.ContainsKey(key))
					_adHocFlagNames.Add(key);
			}
		}

		foreach (var key in Thing.ExtraProperties.Keys)
			if (!schemaKeys.Contains(key)) _adHocFlagNames.Add(key);

		foreach (var name in _adHocFlagNames.OrderBy(f => f))
			AdHocFlags.Add(new AdHocFlagViewModel(name, this));
	}

	public uint GetSpriteIdAtSlot(NyxAssetsEditor.Services.Rendering.ThingAppearanceSlot slot)
	{
		var fg = CurrentFrameGroup;
		var index = fg.GetSpriteIndex(
			slot.InnerW,
			slot.InnerH,
			(uint)SelectedLayer,
			slot.PatternX,
			slot.PatternY,
			_viewPatternZ,
			(uint)SelectedFrame);

		if (index >= fg.SpriteIds.Length)
			return 0;

		return fg.SpriteIds[index];
	}

	private NyxAssetsEditor.Services.Rendering.ThingAppearanceSlot? _selectedSlot;
	public NyxAssetsEditor.Services.Rendering.ThingAppearanceSlot? SelectedSlot
	{
		get => _selectedSlot;
		set
		{
			if (SetProperty(ref _selectedSlot, value))
			{
				RefreshAppearance();
			}
		}
	}

	public double LastMouseX { get; set; }
	public double LastMouseY { get; set; }

	private bool _showSetSpriteIdPrompt;
	public bool ShowSetSpriteIdPrompt
	{
		get => _showSetSpriteIdPrompt;
		set => SetProperty(ref _showSetSpriteIdPrompt, value);
	}

	private string _targetSpriteIdText = string.Empty;
	public string TargetSpriteIdText
	{
		get => _targetSpriteIdText;
		set => SetProperty(ref _targetSpriteIdText, value);
	}

	private static uint _copiedSpriteId;
	private static bool _hasCopiedSprite;

	public bool CanPasteSpriteId => _hasCopiedSprite;

	[RelayCommand]
	private void OpenSetSpriteIdPrompt()
	{
		if (SelectedSlot is { } slot)
		{
			var currentId = GetSpriteIdAtSlot(slot);
			TargetSpriteIdText = currentId.ToString();
			ShowSetSpriteIdPrompt = true;
		}
	}

	[RelayCommand]
	private void CancelSetSpriteId()
	{
		ShowSetSpriteIdPrompt = false;
		TargetSpriteIdText = string.Empty;
	}

	[RelayCommand]
	private void ConfirmSetSpriteId()
	{
		ShowSetSpriteIdPrompt = false;
		if (uint.TryParse(TargetSpriteIdText.Trim(), out var spriteId) && SelectedSlot is { } slot)
		{
			var fg = CurrentFrameGroup;
			var index = fg.GetSpriteIndex(
				slot.InnerW,
				slot.InnerH,
				(uint)SelectedLayer,
				slot.PatternX,
				slot.PatternY,
				_viewPatternZ,
				(uint)SelectedFrame);

			if (index < fg.SpriteIds.Length)
			{
				fg.SpriteIds[index] = spriteId;
				ApplyToCatalog();
				RefreshAppearance();
			}
		}
		TargetSpriteIdText = string.Empty;
	}

	public async void CopySlot(NyxAssetsEditor.Services.Rendering.ThingAppearanceSlot slot)
	{
		_copiedSpriteId = GetSpriteIdAtSlot(slot);
		_hasCopiedSprite = true;
		OnPropertyChanged(nameof(CanPasteSpriteId));

		if (_copiedSpriteId != 0 && SourcePanel.LinkedSpritePanel != null)
		{
			try
			{
				var pixels = SourcePanel.LinkedSpritePanel.Loader.LoadSpritePixels(_copiedSpriteId);
				await NyxAssetsEditor.Services.ImportExport.SpriteClipboard.CopyAsync(pixels);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Failed to copy slot sprite to system clipboard: {ex.Message}");
			}
		}
	}

	public void PasteSlot(NyxAssetsEditor.Services.Rendering.ThingAppearanceSlot slot)
	{
		if (!_hasCopiedSprite)
			return;

		var fg = CurrentFrameGroup;
		var index = fg.GetSpriteIndex(
			slot.InnerW,
			slot.InnerH,
			(uint)SelectedLayer,
			slot.PatternX,
			slot.PatternY,
			_viewPatternZ,
			(uint)SelectedFrame);

		if (index < fg.SpriteIds.Length)
		{
			fg.SpriteIds[index] = _copiedSpriteId;
			ApplyToCatalog();
			RefreshAppearance();
		}
	}

	public void ClearSlot(NyxAssetsEditor.Services.Rendering.ThingAppearanceSlot slot)
	{
		var fg = CurrentFrameGroup;
		var index = fg.GetSpriteIndex(
			slot.InnerW,
			slot.InnerH,
			(uint)SelectedLayer,
			slot.PatternX,
			slot.PatternY,
			_viewPatternZ,
			(uint)SelectedFrame);

		if (index < fg.SpriteIds.Length)
		{
			fg.SpriteIds[index] = 0;
			ApplyToCatalog();
			RefreshAppearance();
		}
	}

	[RelayCommand]
	private void CopySelectedSlot()
	{
		if (SelectedSlot is { } slot)
			CopySlot(slot);
	}

	[RelayCommand]
	private void PasteSelectedSlot()
	{
		if (SelectedSlot is { } slot)
			PasteSlot(slot);
	}

	[RelayCommand]
	private void ClearSelectedSlot()
	{
		if (SelectedSlot is { } slot)
			ClearSlot(slot);
	}

	public void NavigateToSprite(uint spriteId)
	{
		var spritePanel = SourcePanel.LinkedSpritePanel;
		if (spritePanel == null)
			return;

		spritePanel.IsVisible = true;
		spritePanel.IsMinimized = false;
		spritePanel.GoToSpriteId(spriteId);
	}

	public void RequestApplyToCatalog() => ApplyToCatalog();

	private string? FindTomlFile(string fileName)
	{
		string versionDirName = DatVersion.ToString().ToLowerInvariant();
		string relativePath = System.IO.Path.Combine("Assets", "datProtocols", versionDirName, fileName);
		string rootRelativePath = System.IO.Path.Combine("Assets", "datProtocols", fileName);

		// 1. Next to executable
		string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
		if (System.IO.File.Exists(path)) return path;
		path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, rootRelativePath);
		if (System.IO.File.Exists(path)) return path;

		// 2. Working directory (project root in development)
		path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), relativePath);
		if (System.IO.File.Exists(path)) return path;
		path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), rootRelativePath);
		if (System.IO.File.Exists(path)) return path;

		// 3. Up from output directory
		path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", relativePath);
		if (System.IO.File.Exists(path)) return path;
		path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", rootRelativePath);
		if (System.IO.File.Exists(path)) return path;

		return null;
	}

	private void LoadProtocolFlags()
	{
		string tomlText = "";
		string? overridePath = FindTomlFile("flags_override.toml");
		if (overridePath != null)
		{
			try { tomlText = System.IO.File.ReadAllText(overridePath); } catch { }
		}

		if (string.IsNullOrEmpty(tomlText))
		{
			string? defaultPath = FindTomlFile("flags.toml");
			if (defaultPath != null)
			{
				try { tomlText = System.IO.File.ReadAllText(defaultPath); } catch { }
			}
		}

		if (string.IsNullOrEmpty(tomlText))
		{
			try
			{
				string versionDirName = DatVersion.ToString().ToLowerInvariant();
				using (var stream = Avalonia.Platform.AssetLoader.Open(new Uri($"avares://NyxAssetsEditor/Assets/datProtocols/{versionDirName}/flags.toml")))
				using (var reader = new System.IO.StreamReader(stream))
				{
					tomlText = reader.ReadToEnd();
				}
			}
			catch
			{
			}
		}

		if (!string.IsNullOrEmpty(tomlText))
		{
			try
			{
				var model = Tomlyn.TomlSerializer.Deserialize<FlagsTomlModel>(tomlText);
				if (model != null && model.flags != null && model.flags.Count > 0)
				{
					_loadedFlags = model.flags.ToDictionary(pair => pair.Key, pair => pair.Value.label ?? pair.Key, StringComparer.Ordinal);
					_loadedFlagDescriptions = model.flags
						.Where(pair => !string.IsNullOrWhiteSpace(pair.Value.description))
						.ToDictionary(pair => pair.Key, pair => pair.Value.description!, StringComparer.Ordinal);
				}
			}
			catch
			{
			}
		}
		else
		{
			_loadedFlags = GetDefaultFlagsForVersion(DatVersion);
		}

		LoadProtocolProperties();

		OnPropertyChanged(nameof(FlagVisibility));
		OnPropertyChanged(nameof(FlagLabel));
		OnPropertyChanged(nameof(FlagTooltip));
	}

	private static readonly Dictionary<string, string> _defaultFlagDescriptions = new(StringComparer.Ordinal)
	{
		["IsContainer"] = "Allows item to store other items inside container window.",
		["Stackable"] = "Allows items of the same type to stack together.",
		["ForceUse"] = "Forces use action even when right-clicking in non-usable context.",
		["MultiUse"] = "Requires target selection when used (e.g. rope, shovel, potion).",
		["IsFluidContainer"] = "Can hold liquid types such as water, wine, mana, or blood.",
		["IsFluid"] = "Represents pooled or splashable fluid on ground/containers.",
		["IsUnpassable"] = "Blocks creatures and players from walking over tile/item.",
		["IsUnmoveable"] = "Prevents item from being moved or dragged by players.",
		["BlockMissile"] = "Blocks distance attacks, arrows, and magic missiles.",
		["BlockPathfind"] = "Prevents map pathfinding algorithm from routing through tile.",
		["Pickupable"] = "Can be moved into player inventory or container.",
		["Rotatable"] = "Can be rotated using context menu or rotate hotkey.",
		["IsLyingObject"] = "Renders object flat on the ground plane without height layer.",
		["IsFullGround"] = "Covers entire tile area as solid opaque ground surface.",
		["NoMoveAnimation"] = "Suppresses movement interpolation frame animation when moved.",
		["Hangable"] = "Can be hung on wall surfaces.",
		["IsHorizontal"] = "Snaps to east-facing wall hook surface.",
		["IsVertical"] = "Snaps to south-facing wall hook surface.",
		["DontHide"] = "Prevents creature/item occlusion fading when behind objects.",
		["IsTranslucent"] = "Renders with alpha transparency / semi-translucent effect.",
		["IgnoreLook"] = "Prevents inspection or look action when targeted.",
		["Usable"] = "Directly usable item without requiring target selection.",
		["Wrappable"] = "Can be packaged or wrapped into a parcel/furniture kit.",
		["Unwrappable"] = "Can be unwrapped from parcel or kit into placed object.",
		["FloorChange"] = "Triggers floor transition when walked on (stairs, hole, ladder).",
		["BottomEffect"] = "Renders effect texture over top layer rather than ground.",
		["AnimateAlways"] = "Continuously animates even when static or off-screen.",
		["DontCenterOutfit"] = "Disables default centering logic for outfit sprites.",
		["Light"] = "Emits light with specified radius and color intensity.",
		["Offset"] = "Visual rendering offset in X and Y pixels on screen.",
		["Elevation"] = "Height offset in pixels applied to items placed above ground.",
		["MinimapColor"] = "Color code displayed on automap/minimap.",
		["Market"] = "Market category, trade status, required level, and vocation filters.",
	};

	private void LoadProtocolProperties()
	{
		string propTomlText = "";
		string? propPath = FindTomlFile("properties.toml");
		if (propPath != null)
		{
			try { propTomlText = System.IO.File.ReadAllText(propPath); } catch { }
		}

		if (string.IsNullOrEmpty(propTomlText))
		{
			try
			{
				using (var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://NyxAssetsEditor/Assets/datProtocols/properties.toml")))
				using (var reader = new System.IO.StreamReader(stream))
				{
					propTomlText = reader.ReadToEnd();
				}
			}
			catch
			{
			}
		}

		if (!string.IsNullOrEmpty(propTomlText))
		{
			try
			{
				var model = Tomlyn.TomlSerializer.Deserialize<PropertiesTomlModel>(propTomlText);
				if (model?.properties != null)
				{
					_loadedProperties = model.properties;
				}
			}
			catch
			{
			}
		}
	}

	public static Dictionary<string, byte>? GetCustomFlagWriteMap(uint clientVersion)
	{
		var datVersion = DatThingFormatRules.SelectFromClientVersion(new ClientDataVersion { Value = clientVersion });
		string versionDirName = datVersion.ToString().ToLowerInvariant();
		string baseDir = AppDomain.CurrentDomain.BaseDirectory;
		string relativePath = System.IO.Path.Combine("Assets", "datProtocols", versionDirName);

		string? FindPath(string fileName)
		{
			string p = System.IO.Path.Combine(baseDir, relativePath, fileName);
			if (System.IO.File.Exists(p)) return p;
			p = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), relativePath, fileName);
			if (System.IO.File.Exists(p)) return p;
			p = System.IO.Path.Combine(baseDir, "..", "..", "..", relativePath, fileName);
			if (System.IO.File.Exists(p)) return p;
			return null;
		}

		string tomlText = "";
		string? overridePath = FindPath("flags_override.toml");
		if (overridePath != null)
		{
			try { tomlText = System.IO.File.ReadAllText(overridePath); } catch { }
		}

		if (string.IsNullOrEmpty(tomlText))
		{
			string? defaultPath = FindPath("flags.toml");
			if (defaultPath != null)
			{
				try { tomlText = System.IO.File.ReadAllText(defaultPath); } catch { }
			}
		}

		if (string.IsNullOrEmpty(tomlText))
		{
			try
			{
				using (var stream = Avalonia.Platform.AssetLoader.Open(new Uri($"avares://NyxAssetsEditor/Assets/datProtocols/{versionDirName}/flags.toml")))
				using (var reader = new System.IO.StreamReader(stream))
				{
					tomlText = reader.ReadToEnd();
				}
			}
			catch
			{
			}
		}

		if (!string.IsNullOrEmpty(tomlText))
		{
			try
			{
				var model = Tomlyn.TomlSerializer.Deserialize<FlagsTomlModel>(tomlText);
				if (model != null && model.flags != null && model.flags.Count > 0)
				{
					return model.flags.ToDictionary(pair => pair.Key, pair => pair.Value.id, StringComparer.Ordinal);
				}
			}
			catch
			{
			}
		}

		return null;
	}

	private Dictionary<string, string> GetDefaultFlagsForVersion(DatVersionFormat version)
	{
		var flags = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["IsContainer"] = "Container",
			["ForceUse"] = "Force Use",
			["MultiUse"] = "Multi Use",
			["IsFluidContainer"] = "Fluid Container",
			["IsFluid"] = "Fluid",
			["IsUnpassable"] = "Unpassable",
			["IsUnmoveable"] = "Unmoveable",
			["BlockMissile"] = "Block Missile",
			["BlockPathfind"] = "Block Pathfinder",
			["Pickupable"] = "Pickupable",
			["Rotatable"] = "Rotatable",
			["IsLyingObject"] = "Lying Object",
			["IsFullGround"] = "Full Ground",
		};

		if (version == DatVersionFormat.V1)
		{
			flags["Wrappable"] = "Wrappable";
			flags["Unwrappable"] = "Unwrappable";
			flags["FloorChange"] = "Floor Change";
		}
		else if (version == DatVersionFormat.V2)
		{
			flags["Hangable"] = "Hangable";
			flags["IsHorizontal"] = "Hook East";
			flags["IsVertical"] = "Hook South";
			flags["Wrappable"] = "Wrappable";
			flags["Unwrappable"] = "Unwrappable";
			flags["FloorChange"] = "Floor Change";
		}
		else if (version == DatVersionFormat.V3)
		{
			flags["Hangable"] = "Hangable";
			flags["IsHorizontal"] = "Hook East";
			flags["IsVertical"] = "Hook South";
			flags["FloorChange"] = "Floor Change";
		}
		else if (version == DatVersionFormat.V4)
		{
			flags["Hangable"] = "Hangable";
			flags["IsHorizontal"] = "Hook East";
			flags["IsVertical"] = "Hook South";
			flags["DontHide"] = "Don't Hide";
			flags["IgnoreLook"] = "Ignore Look";
			flags["FloorChange"] = "Floor Change";
		}
		else if (version == DatVersionFormat.V5)
		{
			flags["Hangable"] = "Hangable";
			flags["IsHorizontal"] = "Hook East";
			flags["IsVertical"] = "Hook South";
			flags["DontHide"] = "Don't Hide";
			flags["IsTranslucent"] = "Translucent";
			flags["IgnoreLook"] = "Ignore Look";
		}
		else if (version == DatVersionFormat.V6)
		{
			flags["NoMoveAnimation"] = "No Move Animation";
			flags["Hangable"] = "Hangable";
			flags["IsHorizontal"] = "Hook East";
			flags["IsVertical"] = "Hook South";
			flags["DontHide"] = "Don't Hide";
			flags["IsTranslucent"] = "Translucent";
			flags["IgnoreLook"] = "Ignore Look";
			flags["Usable"] = "Useable";
			flags["Wrappable"] = "Wrappable";
			flags["Unwrappable"] = "Unwrappable";
			flags["BottomEffect"] = "Top Effect";
		}

		return flags;
	}

	private static readonly Dictionary<string, string> _defaultLabels = new(StringComparer.Ordinal)
	{
		["IsContainer"] = "Container",
		["Stackable"] = "Stackable",
		["ForceUse"] = "Force Use",
		["MultiUse"] = "Multi Use",
		["IsFluidContainer"] = "Fluid Container",
		["IsFluid"] = "Fluid",
		["IsUnpassable"] = "Unpassable",
		["IsUnmoveable"] = "Unmoveable",
		["BlockMissile"] = "Block Missile",
		["BlockPathfind"] = "Block Pathfinder",
		["FloorChange"] = "Floor Change",
		["NoMoveAnimation"] = "No Move Animation",
		["Pickupable"] = "Pickupable",
		["Hangable"] = "Hangable",
		["IsHorizontal"] = "Hook East",
		["IsVertical"] = "Hook South",
		["Rotatable"] = "Rotatable",
		["DontHide"] = "Don't Hide",
		["IsTranslucent"] = "Translucent",
		["IsLyingObject"] = "Lying Object",
		["IsFullGround"] = "Full Ground",
		["IgnoreLook"] = "Ignore Look",
		["Usable"] = "Useable",
		["Wrappable"] = "Wrappable",
		["Unwrappable"] = "Unwrappable",
		["BottomEffect"] = "Top Effect",
		["AnimateAlways"] = "Animate Always",
		["DontCenterOutfit"] = "Don't Center Outfit",
	};
}

