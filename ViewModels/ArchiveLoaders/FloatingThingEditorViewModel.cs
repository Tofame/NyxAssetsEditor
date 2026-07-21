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

	public FloatingThingEditorViewModel(FloatingThingsLoaderViewModel source, ThingType thing, bool useDetachedThing = false)
	{
		SourcePanel = source;
		UseDetachedThing = useDetachedThing;
		RequestClose += _ =>
		{
			StopAnimationPreview(restoreFrame: false);
			SettingsViewModel.ThingEditorAppearanceSettingsChanged -= OnAppearanceSettingsChanged;
		};
		SettingsViewModel.ThingEditorAppearanceSettingsChanged += OnAppearanceSettingsChanged;
		LoadThing(thing);
		PanelWidth = 540;
		ContentHeight = 680;
		PositionX = source.PositionX + 40;
		PositionY = source.PositionY + 40;
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

		NotifyThingProperties();
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
	public bool ShowOutfitDirections => IsOutfit;
	public bool ShowMissileDirections => false;
	public bool ShowLayerSlider => CurrentFrameGroup.Layers > 1;
	public bool UsesOutfitFrameGroups => IsOutfit && OutfitFrameGroupsEnabled && Thing.FrameGroups.Count > 1;
	public bool ShowFrameSlider => CurrentFrameGroup.Frames > 1 && (!UsesOutfitFrameGroups || SelectedFrameGroupIndex > 0);
	public bool IsAnimationPlaying
	{
		get => _isAnimationPlaying;
		private set => SetProperty(ref _isAnimationPlaying, value);
	}
	public bool ShowPatternGrid => !IsOutfit && !IsMissile;
	public bool ShowPatternXSlider => false;
	public bool ShowPatternYSlider => false;
	public bool ShowPatternZSlider => CurrentFrameGroup.PatternZ > 1;
	public bool ShowAddonSlider => IsOutfit && CurrentFrameGroup.PatternY > 1;
	public bool ShowFrameGroupSlider => UsesOutfitFrameGroups;
	public bool ShowAnimationSection => ShowFrameSlider;
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

		var delayMs = Math.Max(16, GetFrameDelayMs(frameIndex));
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
	public bool ShowMarket => DatVersion >= DatVersionFormat.V5;
	public bool ShowHasDefaultAction => DatVersion >= DatVersionFormat.V6;
	public bool ShowWrappable => DatVersion == DatVersionFormat.V1 || DatVersion == DatVersionFormat.V2 || DatVersion >= DatVersionFormat.V5;
	public bool ShowUnwrappable => DatVersion == DatVersionFormat.V1 || DatVersion == DatVersionFormat.V2 || DatVersion >= DatVersionFormat.V5;
	public bool ShowBottomEffect => DatVersion == DatVersionFormat.V1 || DatVersion == DatVersionFormat.V2 || DatVersion >= DatVersionFormat.V5;
	public bool ShowDontCenterOutfit => DatVersion >= DatVersionFormat.V5;
	public bool ShowUsable => DatVersion >= DatVersionFormat.V6;

	public ObservableCollection<CustomFlagViewModel> CustomFlags { get; } = new();
	private readonly System.Collections.Generic.HashSet<string> _possibleFlags = new();
	private string _newFlagName = string.Empty;

	public string NewFlagName
	{
		get => _newFlagName;
		set => SetProperty(ref _newFlagName, value);
	}

	[RelayCommand]
	public void AddCustomFlag()
	{
		if (string.IsNullOrWhiteSpace(NewFlagName)) return;
		string name = NewFlagName.Trim();
		if (!_possibleFlags.Contains(name))
		{
			_possibleFlags.Add(name);
			CustomFlags.Add(new CustomFlagViewModel(name, this));
		}
		Thing.ExtraProperties[name] = "true";
		NewFlagName = string.Empty;
		ApplyToCatalog();
	}

	public void RemoveCustomFlag(string flagName)
	{
		_possibleFlags.Remove(flagName);
		var vm = CustomFlags.FirstOrDefault(f => f.Name == flagName);
		if (vm != null) CustomFlags.Remove(vm);
		Thing.ExtraProperties.Remove(flagName);
		ApplyToCatalog();
	}

	public void RefreshCustomFlags()
	{
		_possibleFlags.Clear();
		CustomFlags.Clear();
		if (IsJson && SourcePanel.Catalog != null)
		{
			foreach (var t in SourcePanel.Catalog.EnumerateItems())
				foreach (var key in t.ExtraProperties.Keys)
					_possibleFlags.Add(key);
			foreach (var t in SourcePanel.Catalog.EnumerateOutfits())
				foreach (var key in t.ExtraProperties.Keys)
					_possibleFlags.Add(key);
			foreach (var t in SourcePanel.Catalog.EnumerateEffects())
				foreach (var key in t.ExtraProperties.Keys)
					_possibleFlags.Add(key);
			foreach (var t in SourcePanel.Catalog.EnumerateMissiles())
				foreach (var key in t.ExtraProperties.Keys)
					_possibleFlags.Add(key);

			foreach (var key in Thing.ExtraProperties.Keys)
				_possibleFlags.Add(key);

			foreach (var flag in _possibleFlags.OrderBy(f => f))
			{
				CustomFlags.Add(new CustomFlagViewModel(flag, this));
			}
		}
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
}

public partial class CustomFlagViewModel : ViewModelBase
{
	private readonly string _name;
	private readonly FloatingThingEditorViewModel _editor;

	public string Name => _name;

	public bool IsChecked
	{
		get => _editor.Thing.ExtraProperties.ContainsKey(_name) && 
			   _editor.Thing.ExtraProperties[_name].Equals("true", StringComparison.OrdinalIgnoreCase);
		set
		{
			if (value)
			{
				_editor.Thing.ExtraProperties[_name] = "true";
			}
			else
			{
				_editor.Thing.ExtraProperties.Remove(_name);
			}
			OnPropertyChanged();
			_editor.RequestApplyToCatalog();
		}
	}

	public CustomFlagViewModel(string name, FloatingThingEditorViewModel editor)
	{
		_name = name;
		_editor = editor;
	}

	[RelayCommand]
	private void Remove()
	{
		_editor.RemoveCustomFlag(_name);
	}
}
