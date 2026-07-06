using System;
using System.Collections.ObjectModel;
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

namespace NyxAssetsEditor.ViewModels.ArchiveLoaders;

public partial class FloatingThingEditorViewModel : PanelViewModelBase
{
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

	public FloatingThingEditorViewModel(FloatingThingsLoaderViewModel source, ThingType thing)
	{
		SourcePanel = source;
		RequestClose += _ => StopAnimationPreview(restoreFrame: false);
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
		_thing = SourcePanel.GetThingType(thing.Id) ?? thing;
		_selectedFrame = 0;
		_selectedLayer = 0;
		_viewPatternX = 0;
		_viewPatternY = 0;
		_viewPatternZ = 0;
		_outfitDirection = Direction4.South;
		_missileDirection = Direction8.South;

		NotifyThingProperties();
		NotifyAppearanceControls();

		_selectedFrameGroupIndex = 0;
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
	public bool ShowMissileDirections => IsMissile;
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

		var start = Math.Clamp(AnimationStartFrame, 0, FrameMaximum);
		var end = FrameMaximum;
		var next = SelectedFrame + _animationDirection;

		if (IsPingPongStrategy)
		{
			if (next > end)
			{
				_animationDirection = -1;
				next = Math.Max(start, end - 1);
			}
			else if (next < start)
			{
				_animationDirection = 1;
				next = Math.Min(end, start + 1);
			}
		}
		else if (next > end)
		{
			next = start;
		}
		else if (next < start)
		{
			next = end;
		}

		SelectedFrame = next;
		ArmAnimationTimer(next);
	}

	private uint GetFrameDelayMs(int frameIndex)
	{
		var timing = GetFrameTiming(frameIndex);
		if (timing != null)
			return (timing.Value.MinimumMilliseconds + timing.Value.MaximumMilliseconds) / 2;

		return SettingsViewModel.GetDefaultAnimationDurationMs(Kind);
	}

	private AnimationFrameTiming? GetFrameTiming(int frameIndex)
	{
		if (CurrentFrameGroup.FrameTimings == null || frameIndex < 0 || frameIndex >= CurrentFrameGroup.FrameTimings.Length)
			return null;

		return CurrentFrameGroup.FrameTimings[frameIndex];
	}

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
	}

	private void NotifyThingProperties()
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
			return;
		}

		var options = new ThingAppearanceOptions
		{
			FrameGroupIndex = SelectedFrameGroupIndex,
			Layer = SelectedLayer,
			Frame = SelectedFrame,
			PatternX = _viewPatternX,
			PatternY = _viewPatternY,
			PatternZ = _viewPatternZ,
			ShowGrid = ShowGrid,
			ShowCropSize = ShowCropSize,
		};

		var fg = CurrentFrameGroup;
		var edge = SpritePixelCodec.SpriteEdgeLength;
		byte[]? rgba;
		int w;
		int h;

		if (ShowPatternGrid)
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

		if (rgba == null)
		{
			AppearanceImage = null;
			return;
		}

		AppearanceImage = _renderer.ConvertRgba(w, h, rgba);
	}

	private void ApplyToCatalog() => SourcePanel.ApplyThingEdit(Thing);

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
}
