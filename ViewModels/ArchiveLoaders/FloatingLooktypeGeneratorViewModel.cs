using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NyxAssets.Things;
using NyxAssetsEditor.Models.Looktypes;
using NyxAssetsEditor.Services.Looktypes;
using NyxAssetsEditor.Services.Rendering;
using NyxAssetsEditor.Services.Things;
using NyxAssetsEditor.ViewModels.Core;
using NyxAssetsEditor.ViewModels.Pages;

namespace NyxAssetsEditor.ViewModels.ArchiveLoaders;

public enum LooktypeColorPart { Head, Body, Legs, Feet }

public sealed class LooktypeArchivePairViewModel
{
	public LinkedArchivePair Pair { get; }
	public string SpritePath => Pair.SpritePanel.FilePath;
	public string ThingsPath => Pair.ThingsPanel.FilePath;
	public string DisplayName => $"{Pair.ThingsPanel.FileName} + {Pair.SpritePanel.FileName}";
	public LooktypeArchivePairViewModel(LinkedArchivePair pair) => Pair = pair;
}

public sealed class LooktypeColorCellViewModel : ObservableObject
{
	public byte Id { get; }
	public string Hex { get; }
	private bool _isSelected;
	public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
	public LooktypeColorCellViewModel(TibiaOutfitColor color) { Id = color.Id; Hex = color.Hex; }
}

public sealed class LooktypeAddonOptionViewModel : ObservableObject
{
	private readonly Action _changed;
	public int Number { get; }
	public byte Bit => (byte)(1 << (Number - 1));
	private bool _isChecked;
	public bool IsChecked { get => _isChecked; set { if (SetProperty(ref _isChecked, value)) _changed(); } }
	public LooktypeAddonOptionViewModel(int number, bool selected, Action changed) { Number = number; _isChecked = selected; _changed = changed; }
}

public partial class FloatingLooktypeGeneratorViewModel : PanelViewModelBase, IDisposable, IThingFinderContextActionProvider
{
	private const int MinimumPreviewIntervalMs = 16;
	private readonly AssetsViewModel _parent;
	private readonly SpriteRenderer _bitmapRenderer = new();
	private readonly DispatcherTimer _animationTimer = new();
	private readonly DispatcherTimer _rotationTimer = new();
	private int _animationDirection = 1;
	private int _animationStartFrame;
	private bool _isPingPongAnimation;
	private bool _manualPhasePreview;
	private bool _loading;
	private bool _refreshingArchiveChoices;
	private LooktypeProfile _working = new();
	private string? _serviceMessage;

	public string Title => "Looktype Generator";
	public ObservableCollection<LooktypeArchivePairViewModel> ArchivePairs { get; } = new();
	public ObservableCollection<uint> AppearanceIds { get; } = new();
	public ObservableCollection<uint> MountIds { get; } = new();
	public ObservableCollection<uint> CorpseIds { get; } = new();
	public ObservableCollection<LooktypeAddonOptionViewModel> AddonOptions { get; } = new();
	public ObservableCollection<LooktypeColorCellViewModel> HeadColors { get; } = new();
	public ObservableCollection<LooktypeColorCellViewModel> BodyColors { get; } = new();
	public ObservableCollection<LooktypeColorCellViewModel> LegColors { get; } = new();
	public ObservableCollection<LooktypeColorCellViewModel> FeetColors { get; } = new();

	private LooktypeArchivePairViewModel? _selectedArchivePair;
	public LooktypeArchivePairViewModel? SelectedArchivePair
	{
		get => _selectedArchivePair;
		set { if (SetProperty(ref _selectedArchivePair, value)) { RefreshArchiveChoices(); RefreshPreview(); if (!_loading) _parent.TriggerSaveAppState(); } }
	}

	private bool _isOutfitMode = true;
	public bool IsOutfitMode
	{
		get => _isOutfitMode;
		set
		{
			if (!SetProperty(ref _isOutfitMode, value)) return;
			OnPropertyChanged(nameof(IsItemMode));
			Changed(p => p.AppearanceKind = value ? LooktypeAppearanceKind.Outfit : LooktypeAppearanceKind.Item);
			_selectedAppearanceId = value ? _working.LookType : _working.LookTypeEx;
			OnPropertyChanged(nameof(SelectedAppearanceId));
			RefreshArchiveChoices(); RefreshPreview();
		}
	}
	public bool IsItemMode { get => !IsOutfitMode; set { if (value) IsOutfitMode = false; } }
	public byte HeadColorId => _working.Head;
	public byte BodyColorId => _working.Body;
	public byte LegColorId => _working.Legs;
	public byte FeetColorId => _working.Feet;
	public string HeadColorHex => TibiaOutfitPalette.Get(_working.Head).Hex;
	public string BodyColorHex => TibiaOutfitPalette.Get(_working.Body).Hex;
	public string LegColorHex => TibiaOutfitPalette.Get(_working.Legs).Hex;
	public string FeetColorHex => TibiaOutfitPalette.Get(_working.Feet).Hex;

	private LooktypeColorPart _activeColorPart;
	public bool IsHeadPalette => _activeColorPart == LooktypeColorPart.Head;
	public bool IsBodyPalette => _activeColorPart == LooktypeColorPart.Body;
	public bool IsLegsPalette => _activeColorPart == LooktypeColorPart.Legs;
	public bool IsFeetPalette => _activeColorPart == LooktypeColorPart.Feet;
	public IEnumerable<LooktypeColorCellViewModel> ActiveColors => _activeColorPart switch
	{
		LooktypeColorPart.Body => BodyColors,
		LooktypeColorPart.Legs => LegColors,
		LooktypeColorPart.Feet => FeetColors,
		_ => HeadColors,
	};
	public string ActiveColorTitle => _activeColorPart switch
	{
		LooktypeColorPart.Body => $"Body \u2014 {_working.Body}",
		LooktypeColorPart.Legs => $"Legs \u2014 {_working.Legs}",
		LooktypeColorPart.Feet => $"Feet \u2014 {_working.Feet}",
		_ => $"Head \u2014 {_working.Head}",
	};

	private uint? _selectedAppearanceId;
	public uint? SelectedAppearanceId
	{
		get => _selectedAppearanceId;
		set
		{
			if (_refreshingArchiveChoices) return;
			if (!SetProperty(ref _selectedAppearanceId, value) || !value.HasValue) return;
			Changed(p => { if (IsOutfitMode) p.LookType = value.Value; else p.LookTypeEx = value.Value; });
			UpdateAddonOptions(); RefreshPreview();
		}
	}

	private uint? _selectedMountId;
	public uint? SelectedMountId
	{
		get => _selectedMountId;
		set
		{
			if (_refreshingArchiveChoices) return;
			if (SetProperty(ref _selectedMountId, value)) { Changed(p => p.Mount = value ?? 0); RefreshPreview(); }
		}
	}

	private uint? _selectedCorpseId;
	public uint? SelectedCorpseId
	{
		get => _selectedCorpseId;
		set
		{
			if (_refreshingArchiveChoices) return;
			if (SetProperty(ref _selectedCorpseId, value)) { Changed(profile => profile.Corpse = value ?? 0); if (_previewCorpse) RefreshPreview(); }
		}
	}

	private int _walkIntervalMs;
	public int WalkIntervalMs { get => _walkIntervalMs; set { value = Math.Max(0, value); if (SetProperty(ref _walkIntervalMs, value)) { Changed(p => p.WalkIntervalMs = value); RefreshPreview(); } } }
	private int _rotationIntervalMs = 1000;
	public int RotationIntervalMs { get => _rotationIntervalMs; set { value = Math.Max(MinimumPreviewIntervalMs, value); if (SetProperty(ref _rotationIntervalMs, value)) { Changed(p => p.RotationIntervalMs = value); RestartRotation(); } } }

	private bool _animationEnabled;
	public bool AnimationEnabled { get => _animationEnabled; set { if (SetProperty(ref _animationEnabled, value)) { _animationDirection = 1; _manualPhasePreview = false; Changed(p => p.AnimationEnabled = value); if (!value) _animationTimer.Stop(); RefreshPreview(); } } }
	private bool _autoRotate;
	public bool AutoRotate { get => _autoRotate; set { if (SetProperty(ref _autoRotate, value)) { Changed(p => p.AutoRotate = value); RestartRotation(); } } }
	private bool _includePreviewSettings;
	public bool IncludePreviewSettings
	{
		get => _includePreviewSettings;
		set { if (SetProperty(ref _includePreviewSettings, value)) Changed(profile => profile.IncludePreviewSettings = value); }
	}
	private int _animationPhase;
	public int AnimationPhase { get => _animationPhase; set { value = Math.Max(0, value); if (SetProperty(ref _animationPhase, value)) { if (!AnimationEnabled && !_loading) _manualPhasePreview = true; Changed(p => p.AnimationPhase = value); RefreshPreview(); } } }
	private int _frameMaximum;
	public int FrameMaximum { get => _frameMaximum; private set => SetProperty(ref _frameMaximum, value); }
	private LooktypeDirection _direction = LooktypeDirection.South;
	public LooktypeDirection Direction { get => _direction; private set { if (SetProperty(ref _direction, value)) { Changed(p => p.Direction = value); NotifyDirection(); RefreshPreview(); } } }
	public bool IsNorth => Direction == LooktypeDirection.North;
	public bool IsEast => Direction == LooktypeDirection.East;
	public bool IsSouth => Direction == LooktypeDirection.South;
	public bool IsWest => Direction == LooktypeDirection.West;

	private WriteableBitmap? _previewImage;
	public WriteableBitmap? PreviewImage { get => _previewImage; private set => SetProperty(ref _previewImage, value); }
	private string _message = "Load an archive pair to preview.";
	public string Message { get => _message; private set => SetProperty(ref _message, value); }
	public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
	private string _appearanceText = string.Empty;
	private bool _appearanceTextIsXml = true;
	private bool _applyingAppearanceText;
	public bool IsAppearanceLua => !_appearanceTextIsXml;
	public bool IsAppearanceXml => _appearanceTextIsXml;
	public string AppearanceText
	{
		get => _appearanceText;
		set
		{
			if (!SetProperty(ref _appearanceText, value)) return;
			ApplyAppearanceText(value);
		}
	}
	private bool _previewCorpse;
	public bool IsPreviewingAppearance => !_previewCorpse;
	public bool IsPreviewingCorpse => _previewCorpse;
	public FloatingLooktypeGeneratorViewModel(AssetsViewModel parent)
	{
		_parent = parent;
		PanelWidth = 900; ContentHeight = 800;
		foreach (var color in TibiaOutfitPalette.Create())
		{
			HeadColors.Add(new(color)); BodyColors.Add(new(color)); LegColors.Add(new(color)); FeetColors.Add(new(color));
		}
		_animationTimer.Tick += OnAnimationTick;
		_rotationTimer.Tick += OnRotationTick;
		SettingsViewModel.LooktypeRendererSettingsChanged += OnLooktypeRendererSettingsChanged;
		RefreshArchivePairs();
		LoadWorking(_working);
		UpdateAppearanceText();
	}

	public void RefreshArchivePairs(string? preferredSpritePath = null, string? preferredThingsPath = null)
	{
		var currentSprite = preferredSpritePath ?? SelectedArchivePair?.SpritePath;
		var currentThings = preferredThingsPath ?? SelectedArchivePair?.ThingsPath;
		ArchivePairs.Clear();
		foreach (var pair in _parent.GetCompilePairs()) ArchivePairs.Add(new(pair));
		SelectedArchivePair = ArchivePairs.FirstOrDefault(p =>
			string.Equals(p.SpritePath, currentSprite, StringComparison.OrdinalIgnoreCase) && string.Equals(p.ThingsPath, currentThings, StringComparison.OrdinalIgnoreCase))
			?? ArchivePairs.FirstOrDefault();
	}

	private void RefreshArchiveChoices()
	{
		var selected = SelectedAppearanceId;
		var selectedMount = _selectedMountId;
		var selectedCorpse = _selectedCorpseId;
		_refreshingArchiveChoices = true;
		try
		{
			AppearanceIds.Clear(); MountIds.Clear(); CorpseIds.Clear();
			var catalog = SelectedArchivePair?.Pair.ThingsPanel.Catalog;
			if (catalog != null)
			{
				var ids = IsOutfitMode ? catalog.EnumerateOutfits().Select(t => t.Id) : catalog.EnumerateItems().Select(t => t.Id);
				foreach (var id in ids) AppearanceIds.Add(id);
				foreach (var id in catalog.EnumerateOutfits().Select(t => t.Id)) MountIds.Add(id);
				foreach (var id in catalog.EnumerateItems().Select(t => t.Id)) CorpseIds.Add(id);
			}
			if (selected.HasValue && selected.Value > 0 && !AppearanceIds.Contains(selected.Value)) AppearanceIds.Add(selected.Value);
			if (selectedMount.HasValue && !MountIds.Contains(selectedMount.Value)) MountIds.Add(selectedMount.Value);
			if (selectedCorpse.HasValue && !CorpseIds.Contains(selectedCorpse.Value)) CorpseIds.Add(selectedCorpse.Value);
			if (selected.HasValue && selected.Value > 0) _selectedAppearanceId = selected;
			else if (AppearanceIds.Count > 0)
			{
				_selectedAppearanceId = AppearanceIds[0];
				if (IsOutfitMode) _working.LookType = AppearanceIds[0]; else _working.LookTypeEx = AppearanceIds[0];
			}
			_selectedMountId = selectedMount;
			_selectedCorpseId = selectedCorpse;
			OnPropertyChanged(nameof(SelectedAppearanceId)); OnPropertyChanged(nameof(SelectedMountId)); OnPropertyChanged(nameof(SelectedCorpseId));
			UpdateAddonOptions();
		}
		finally { _refreshingArchiveChoices = false; }
	}

	private void LoadWorking(LooktypeProfile source)
	{
		_loading = true;
		_working = source.Clone();
		_isOutfitMode = _working.AppearanceKind == LooktypeAppearanceKind.Outfit;
		_selectedAppearanceId = _isOutfitMode ? _working.LookType : _working.LookTypeEx;
		_selectedMountId = _working.Mount == 0 ? null : _working.Mount;
		_selectedCorpseId = _working.Corpse == 0 ? null : _working.Corpse;
		_walkIntervalMs = _working.WalkIntervalMs; _rotationIntervalMs = Math.Max(MinimumPreviewIntervalMs, _working.RotationIntervalMs);
		_animationEnabled = _working.AnimationEnabled; _autoRotate = _working.AutoRotate;
		_includePreviewSettings = _working.IncludePreviewSettings;
		_animationPhase = _working.AnimationPhase; _direction = _working.Direction; _manualPhasePreview = false;
		OnPropertyChanged(string.Empty); NotifyDirection(); RefreshColorSelections(); RefreshArchiveChoices();
		_loading = false; _animationDirection = 1; _animationTimer.Stop(); RestartRotation(); RefreshPreview();
	}

	private void Changed(Action<LooktypeProfile> change)
	{
		if (_loading) return;
		change(_working);
		if (!_applyingAppearanceText) UpdateAppearanceText();
	}

	[RelayCommand]
	private void SetDirection(string direction)
	{
		if (Enum.TryParse<LooktypeDirection>(direction, true, out var parsed)) Direction = parsed;
	}

	[RelayCommand]
	private void ClearMount() => SelectedMountId = null;

	[RelayCommand]
	private void ClearCorpse() => SelectedCorpseId = null;

	[RelayCommand]
	private void ShowColorPalette(string part)
	{
		if (!Enum.TryParse<LooktypeColorPart>(part, true, out var parsed) || _activeColorPart == parsed) return;
		_activeColorPart = parsed;
		NotifyActiveColorPart();
	}

	[RelayCommand]
	private void SetActiveColor(LooktypeColorCellViewModel cell)
	{
		switch (_activeColorPart)
		{
			case LooktypeColorPart.Body: SetColor(cell.Id, p => p.Body = cell.Id); break;
			case LooktypeColorPart.Legs: SetColor(cell.Id, p => p.Legs = cell.Id); break;
			case LooktypeColorPart.Feet: SetColor(cell.Id, p => p.Feet = cell.Id); break;
			default: SetColor(cell.Id, p => p.Head = cell.Id); break;
		}
	}
	private void SetColor(byte id, Action<LooktypeProfile> set) { Changed(set); RefreshColorSelections(); RefreshPreview(); }

	[RelayCommand]
	private void ShowPreviewMode(string mode)
	{
		var corpse = mode.Equals("Corpse", StringComparison.OrdinalIgnoreCase);
		if (_previewCorpse == corpse) return;
		_previewCorpse = corpse;
		_animationDirection = 1;
		_manualPhasePreview = false;
		OnPropertyChanged(nameof(IsPreviewingAppearance)); OnPropertyChanged(nameof(IsPreviewingCorpse));
		RefreshPreview();
	}

	private void ApplyAppearanceText(string text)
	{
		if (_applyingAppearanceText || string.IsNullOrWhiteSpace(text)) return;
		var isXml = text.TrimStart().StartsWith('<');
		if (_appearanceTextIsXml != isXml)
		{
			_appearanceTextIsXml = isXml;
			OnPropertyChanged(nameof(IsAppearanceLua));
			OnPropertyChanged(nameof(IsAppearanceXml));
		}
		var result = _appearanceTextIsXml
			? LooktypeInterchangeService.ImportXml(text)
			: LooktypeInterchangeService.ImportLua(text);
		if (!result.Success)
		{
			_serviceMessage = result.Error;
			RefreshPreview();
			return;
		}

		_serviceMessage = result.Warnings.Count > 0 ? string.Join(" ", result.Warnings) : null;
		_applyingAppearanceText = true;
		try { LoadWorking(result.Profile); }
		finally { _applyingAppearanceText = false; }
	}

	[RelayCommand]
	private void ShowAppearanceFormat(string format)
	{
		var isXml = format.Equals("Xml", StringComparison.OrdinalIgnoreCase);
		if (_appearanceTextIsXml == isXml) return;
		_appearanceTextIsXml = isXml;
		OnPropertyChanged(nameof(IsAppearanceLua));
		OnPropertyChanged(nameof(IsAppearanceXml));
		UpdateAppearanceText();
		RefreshPreview();
	}

	private void UpdateAppearanceText()
	{
		_serviceMessage = null;
		var text = _appearanceTextIsXml
			? LooktypeInterchangeService.ExportXml(_working)
			: LooktypeInterchangeService.ExportLua(_working);
		SetProperty(ref _appearanceText, text, nameof(AppearanceText));
	}

	private void UpdateAddonOptions()
	{
		AddonOptions.Clear();
		var thing = IsOutfitMode ? SelectedArchivePair?.Pair.ThingsPanel.Catalog?.TryGetOutfit(_working.LookType) : null;
		var count = IsOutfitMode
			? thing?.FrameGroups.Count > 0 ? Math.Min(8, Math.Max(0, (int)thing.FrameGroups[0].PatternY - 1)) : 0
			: 8;
		for (var i = 1; i <= count; i++) AddonOptions.Add(new(i, (_working.Addons & (1 << (i - 1))) != 0, OnAddonsChanged));
	}

	private void OnAddonsChanged()
	{
		if (_loading) return;
		var addons = (byte)AddonOptions.Where(a => a.IsChecked).Aggregate(0, (mask, item) => mask | item.Bit);
		Changed(profile => profile.Addons = addons); RefreshPreview();
	}

	private void RefreshColorSelections()
	{
		Select(HeadColors, _working.Head); Select(BodyColors, _working.Body); Select(LegColors, _working.Legs); Select(FeetColors, _working.Feet);
		OnPropertyChanged(nameof(HeadColorId)); OnPropertyChanged(nameof(BodyColorId)); OnPropertyChanged(nameof(LegColorId)); OnPropertyChanged(nameof(FeetColorId));
		OnPropertyChanged(nameof(HeadColorHex)); OnPropertyChanged(nameof(BodyColorHex)); OnPropertyChanged(nameof(LegColorHex)); OnPropertyChanged(nameof(FeetColorHex));
		OnPropertyChanged(nameof(ActiveColorTitle));
		static void Select(IEnumerable<LooktypeColorCellViewModel> cells, byte id) { foreach (var cell in cells) cell.IsSelected = cell.Id == id; }
	}

	private void NotifyActiveColorPart()
	{
		OnPropertyChanged(nameof(IsHeadPalette)); OnPropertyChanged(nameof(IsBodyPalette));
		OnPropertyChanged(nameof(IsLegsPalette)); OnPropertyChanged(nameof(IsFeetPalette));
		OnPropertyChanged(nameof(ActiveColors)); OnPropertyChanged(nameof(ActiveColorTitle));
	}

	private void RefreshPreview()
	{
		_working.AnimationPhase = AnimationPhase; _working.Direction = Direction;
		var pair = SelectedArchivePair?.Pair;
		var renderProfile = _previewCorpse || _manualPhasePreview ? _working.Clone() : _working;
		if (_manualPhasePreview) renderProfile.AnimationEnabled = true;
		if (_previewCorpse)
		{
			renderProfile.AppearanceKind = LooktypeAppearanceKind.Item;
			renderProfile.LookTypeEx = _working.Corpse;
		}
		var fallback = (int)(renderProfile.AppearanceKind == LooktypeAppearanceKind.Outfit
			? SettingsViewModel.OutfitAnimationDurationMs
			: SettingsViewModel.ItemAnimationDurationMs);
		var options = new LooktypeRenderOptions(
			SettingsViewModel.LooktypeMountAlignment,
			SettingsViewModel.LooktypeMountedRiderOffsetX,
			SettingsViewModel.LooktypeMountedRiderOffsetY);
		var result = _previewCorpse && _working.Corpse == 0
			? new LooktypeRenderResult(null, 0, 0, 0, 0, fallback, new[] { "Select a corpse item to preview it." })
			: LooktypeRenderer.Render(renderProfile, pair?.ThingsPanel.Catalog,
				pair?.ThingsPanel.GetActiveSpriteLoader(), fallback, options,
				improvedAnimations: pair?.ThingsPanel.UseFrameAnimations ?? true);
		var old = PreviewImage;
		PreviewImage = result.HasImage ? _bitmapRenderer.ConvertRgba(result.Width, result.Height, result.Pixels!) : null;
		old?.Dispose();
		var availableFrameMaximum = 0;
		var animationThing = renderProfile.AppearanceKind == LooktypeAppearanceKind.Outfit
			? pair?.ThingsPanel.Catalog?.TryGetOutfit(renderProfile.LookType)
			: pair?.ThingsPanel.Catalog?.TryGetItem(renderProfile.LookTypeEx);
		if (animationThing?.FrameGroups.Count > 0)
		{
			var groupIndex = renderProfile.AppearanceKind == LooktypeAppearanceKind.Outfit && animationThing.FrameGroups.Count > 1 ? 1 : 0;
			availableFrameMaximum = Math.Max(0, (int)animationThing.FrameGroups[groupIndex].Frames - 1);
		}
		FrameMaximum = Math.Max(Math.Max(0, result.FrameCount - 1), availableFrameMaximum);
		_animationStartFrame = result.AnimationStartFrame;
		_isPingPongAnimation = result.IsPingPongAnimation;
		if (AnimationPhase > FrameMaximum && FrameMaximum >= 0) { _animationPhase = 0; _working.AnimationPhase = 0; OnPropertyChanged(nameof(AnimationPhase)); }
		var messages = new List<string>(); if (!string.IsNullOrWhiteSpace(_serviceMessage)) messages.Add(_serviceMessage!); messages.AddRange(result.Warnings);
		Message = string.Join(Environment.NewLine, messages.Distinct()); OnPropertyChanged(nameof(HasMessage));
		if (AnimationEnabled)
		{
			var interval = _working.WalkIntervalMs > 0 ? _working.WalkIntervalMs : result.SuggestedDelayMs;
			_animationTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(MinimumPreviewIntervalMs, interval));
			if (!_animationTimer.IsEnabled) _animationTimer.Start();
		}
	}

	private void RestartRotation() { _rotationTimer.Stop(); if (AutoRotate) { _rotationTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(MinimumPreviewIntervalMs, RotationIntervalMs)); _rotationTimer.Start(); } }
	private void OnAnimationTick(object? sender, EventArgs e)
	{
		_animationTimer.Stop();
		if (!AnimationEnabled) return;
		var next = ThingAnimationPlayback.GetNextFrame(AnimationPhase, _animationStartFrame,
			FrameMaximum, _isPingPongAnimation, ref _animationDirection);
		if (SetProperty(ref _animationPhase, next, nameof(AnimationPhase))) RefreshPreview();
		if (AnimationEnabled && !_animationTimer.IsEnabled) _animationTimer.Start();
	}
	private void OnRotationTick(object? sender, EventArgs e)
	{
		if (!AutoRotate) return;
		var next = (LooktypeDirection)(((int)Direction + 1) % 4);
		if (SetProperty(ref _direction, next, nameof(Direction)))
		{
			NotifyDirection();
			RefreshPreview();
		}
	}
	private void OnLooktypeRendererSettingsChanged() => RefreshPreview();

	private void NotifyDirection() { OnPropertyChanged(nameof(IsNorth)); OnPropertyChanged(nameof(IsEast)); OnPropertyChanged(nameof(IsSouth)); OnPropertyChanged(nameof(IsWest)); }

	public string SelectedSpritePath => SelectedArchivePair?.SpritePath ?? string.Empty;
	public string SelectedThingsPath => SelectedArchivePair?.ThingsPath ?? string.Empty;

	public IEnumerable<ThingFinderContextAction> GetThingFinderContextActions(
		FloatingThingsLoaderViewModel source,
		ThingType thing)
	{
		var sourcePair = ArchivePairs.FirstOrDefault(pair => ReferenceEquals(pair.Pair.ThingsPanel, source));
		if (sourcePair == null) yield break;

		if (thing.Kind == ThingKind.Outfit)
		{
			yield return CreateFinderAction("Set as outfit", sourcePair, thing, () =>
			{
				IsOutfitMode = true;
				SelectedAppearanceId = thing.Id;
			});
			yield return CreateFinderAction("Set as mount", sourcePair, thing, () => SelectedMountId = thing.Id);
		}
		else if (thing.Kind == ThingKind.Item)
		{
			yield return CreateFinderAction("Set as corpse", sourcePair, thing, () => SelectedCorpseId = thing.Id);
		}
	}

	private ThingFinderContextAction CreateFinderAction(
		string label,
		LooktypeArchivePairViewModel sourcePair,
		ThingType thing,
		Action assign)
	{
		var requiresSwitch = !ReferenceEquals(SelectedArchivePair?.Pair.ThingsPanel, sourcePair.Pair.ThingsPanel)
			|| !ReferenceEquals(SelectedArchivePair?.Pair.SpritePanel, sourcePair.Pair.SpritePanel);
		Task Execute()
		{
			var isUsingSourcePair = ReferenceEquals(SelectedArchivePair?.Pair.ThingsPanel, sourcePair.Pair.ThingsPanel)
				&& ReferenceEquals(SelectedArchivePair?.Pair.SpritePanel, sourcePair.Pair.SpritePanel);
			if (!isUsingSourcePair) SelectedArchivePair = sourcePair;
			assign();
			return Task.CompletedTask;
		}

		return requiresSwitch
			? new ThingFinderContextAction(
				label,
				Execute,
				"Switch archive pair?",
				$"The Looktype Generator is using a different archive pair. Switch it to {sourcePair.DisplayName} and {label.ToLowerInvariant()} {thing.Id}?",
				"Switch and set")
			: new ThingFinderContextAction(label, Execute);
	}

	public void Dispose()
	{
		_animationTimer.Stop(); _rotationTimer.Stop(); _animationTimer.Tick -= OnAnimationTick; _rotationTimer.Tick -= OnRotationTick;
		SettingsViewModel.LooktypeRendererSettingsChanged -= OnLooktypeRendererSettingsChanged;
		PreviewImage?.Dispose();
	}
}
