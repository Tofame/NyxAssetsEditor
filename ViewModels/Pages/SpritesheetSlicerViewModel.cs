using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using NyxAssets.Things;
using NyxAssetsEditor.Services.ImportExport;
using NyxAssetsEditor.Services.Persistence;
using NyxAssetsEditor.Services.Rendering;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;
using NyxAssetsEditor.ViewModels.Core;

namespace NyxAssetsEditor.ViewModels.Pages;

public sealed class SlicerTargetViewModel
{
	public required FloatingSpriteLoaderViewModel SpritePanel { get; init; }
	public FloatingThingsLoaderViewModel? ThingsPanel { get; init; }
	public bool HasThings => ThingsPanel is { IsArchiveLoaded: true };
	public string DisplayName => HasThings
		? $"{SpritePanel.FileName} ↔ {ThingsPanel!.FileName}"
		: $"{SpritePanel.FileName} (sprites only)";
}

public sealed class SlicerThingChoiceViewModel
{
	public required ThingType Thing { get; init; }
	public string DisplayName => $"#{Thing.Id} — {Thing.Kind}";
}

public sealed class SlicerPreviewViewModel
{
	public required int SourceColumn { get; init; }
	public required int SourceRow { get; init; }
	public required byte[] Pixels { get; init; }
	public required bool IsEmpty { get; init; }
	public required WriteableBitmap Preview { get; init; }
	public string Label => $"({SourceColumn}, {SourceRow})" + (IsEmpty ? " empty" : "");
}

public partial class SpritesheetSlicerViewModel : ViewModelBase, IDisposable
{
	private readonly AssetsViewModel _assets;
	private readonly SpriteRenderer _renderer = new();
	private readonly FloatingSpriteLoaderViewModel? _origin;
	private readonly PersistenceService.SlicerStateModel _state;
	private SlicerImage? _image;
	private SlicerImage? _undoImage;
	private WriteableBitmap? _sheetBitmap;
	private string _sourcePath = "";
	private int _offsetX;
	private int _offsetY;
	private int _columns = 1;
	private int _rows = 1;
	private int _cellSize = 32;
	private double _zoom = 1;
	private bool _subdivisions;
	private bool _includeEmptySprites = true;
	private int _thingWidth;
	private int _thingHeight;
	private int _outfitDirections = 4;
	private int _outfitFrames = 3;
	private uint _templateItemId;
	private uint _replacementThingId;
	private bool _replaceExisting;
	private ThingKind _selectedKind = ThingKind.Item;
	private string _statusMessage = "Open or drop an image to begin.";
	private bool _statusIsError;
	private SlicerTargetViewModel? _selectedTarget;
	private string _templateSearch = "";
	private string _replacementSearch = "";
	private SlicerThingChoiceViewModel? _selectedTemplate;
	private SlicerThingChoiceViewModel? _selectedReplacement;
	private WriteableBitmap? _templatePreview;
	private WriteableBitmap? _replacementPreview;
	private readonly List<SlicerThingChoiceViewModel> _allTemplates = new();
	private readonly List<SlicerThingChoiceViewModel> _allReplacements = new();

	public SpritesheetSlicerViewModel(AssetsViewModel assets, FloatingSpriteLoaderViewModel? origin = null)
	{
		_assets = assets;
		_origin = origin ?? assets.LastActivePair?.SpritePanel;
		_state = PersistenceService.GetSlicerState();
		_subdivisions = _state.Subdivisions;
		_includeEmptySprites = _state.IncludeEmptySprites;
		_thingWidth = Math.Max(0, _state.ThingWidth);
		_thingHeight = Math.Max(0, _state.ThingHeight);
		_outfitDirections = Math.Max(1, _state.OutfitDirections);
		_outfitFrames = Math.Max(1, _state.OutfitFrames);
		_templateItemId = _state.TemplateItemId;
		_replaceExisting = _state.ReplaceExisting;
		if (Enum.TryParse<ThingKind>(_state.ThingKind, out var kind)) _selectedKind = kind;
		_assets.ActivePanels.CollectionChanged += OnPanelsChanged;
		RefreshTargets();
	}

	public ObservableCollection<SlicerTargetViewModel> Targets { get; } = new();
	public ObservableCollection<SlicerPreviewViewModel> CroppedSprites { get; } = new();
	public ObservableCollection<SlicerThingChoiceViewModel> TemplateChoices { get; } = new();
	public ObservableCollection<SlicerThingChoiceViewModel> ReplacementChoices { get; } = new();
	public IReadOnlyList<int> AvailableCellSizes { get; } = new[] { 32 };
	public IReadOnlyList<ThingKind> ThingKinds { get; } = new[] { ThingKind.Item, ThingKind.Outfit, ThingKind.Effect, ThingKind.Missile };

	public SlicerImage? Image => _image;
	public WriteableBitmap? SheetBitmap { get => _sheetBitmap; private set => SetProperty(ref _sheetBitmap, value); }
	public bool HasImage => _image != null;
	public int ImageWidth => _image?.Width ?? 0;
	public int ImageHeight => _image?.Height ?? 0;
	public string SourceFileName => string.IsNullOrEmpty(_sourcePath) ? "No image loaded" : Path.GetFileName(_sourcePath);
	public string LastOpenDirectory => _state.LastOpenDirectory;
	public string LastExportDirectory => _state.LastExportDirectory;

	public int OffsetX { get => _offsetX; set { if (SetProperty(ref _offsetX, value)) ClampAndNotifyGrid(); } }
	public int OffsetY { get => _offsetY; set { if (SetProperty(ref _offsetY, value)) ClampAndNotifyGrid(); } }
	public int Columns { get => _columns; set { if (SetProperty(ref _columns, value)) ClampAndNotifyGrid(); } }
	public int Rows { get => _rows; set { if (SetProperty(ref _rows, value)) ClampAndNotifyGrid(); } }
	public int CellSize { get => _cellSize; set { if (SetProperty(ref _cellSize, value)) ClampAndNotifyGrid(); } }
	public double Zoom { get => _zoom; set => SetProperty(ref _zoom, Math.Clamp(value, 0.1, 5)); }
	public bool Subdivisions { get => _subdivisions; set => SetProperty(ref _subdivisions, value); }
	public bool IncludeEmptySprites { get => _includeEmptySprites; set => SetProperty(ref _includeEmptySprites, value); }
	public int ThingWidth { get => _thingWidth; set { if (SetProperty(ref _thingWidth, Math.Max(0, value))) NotifyValidation(); } }
	public int ThingHeight { get => _thingHeight; set { if (SetProperty(ref _thingHeight, Math.Max(0, value))) NotifyValidation(); } }
	public int OutfitDirections { get => _outfitDirections; set { if (SetProperty(ref _outfitDirections, Math.Max(1, value))) NotifyValidation(); } }
	public int OutfitFrames { get => _outfitFrames; set { if (SetProperty(ref _outfitFrames, Math.Max(1, value))) NotifyValidation(); } }
	public uint TemplateItemId
	{
		get => _templateItemId;
		set { if (SetProperty(ref _templateItemId, value) && SelectedTemplate?.Thing.Id != value) SelectedTemplate = _allTemplates.FirstOrDefault(c => c.Thing.Id == value); }
	}
	public uint ReplacementThingId
	{
		get => _replacementThingId;
		set { if (SetProperty(ref _replacementThingId, value) && SelectedReplacement?.Thing.Id != value) SelectedReplacement = _allReplacements.FirstOrDefault(c => c.Thing.Id == value); }
	}
	public bool ReplaceExisting { get => _replaceExisting; set { if (SetProperty(ref _replaceExisting, value)) { OnPropertyChanged(nameof(IsCreateMode)); NotifyValidation(); } } }
	public bool IsCreateMode => !ReplaceExisting;

	public ThingKind SelectedKind
	{
		get => _selectedKind;
		set
		{
			if (!SetProperty(ref _selectedKind, value)) return;
			OnPropertyChanged(nameof(IsItem)); OnPropertyChanged(nameof(IsOutfit)); OnPropertyChanged(nameof(UsesFootprint));
			RefreshThingChoices(); NotifyValidation();
		}
	}

	public bool IsItem => SelectedKind == ThingKind.Item;
	public bool IsOutfit => SelectedKind == ThingKind.Outfit;
	public bool UsesFootprint => SelectedKind != ThingKind.Outfit;
	public bool IsOutfitLayoutValid => HasImage && Columns % OutfitDirections == 0 && Rows % OutfitFrames == 0;
	public string OutfitLayoutHint => IsOutfitLayoutValid
		? $"Valid: {Columns / OutfitDirections}×{Rows / OutfitFrames} cells, {OutfitDirections} directions, {OutfitFrames} frames"
		: $"Need columns % {OutfitDirections} = 0 and rows % {OutfitFrames} = 0";
	public string SplitHint => ThingWidth == 0 && ThingHeight == 0
		? $"One {Columns}×{Rows} thing"
		: ThingWidth > 0 && ThingHeight > 0 && Columns % ThingWidth == 0 && Rows % ThingHeight == 0
			? $"{(Columns / ThingWidth) * (Rows / ThingHeight)} things, each {ThingWidth}×{ThingHeight}"
			: "Width and height must both be 0, or divide the selection exactly.";

	public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
	public bool StatusIsError { get => _statusIsError; private set => SetProperty(ref _statusIsError, value); }
	public bool CanUndoTransform => _undoImage != null;
	public bool CanCrop => HasImage && Columns > 0 && Rows > 0;
	public bool CanImportRaw => SelectedTarget?.SpritePanel.IsArchiveLoaded == true && CroppedSprites.Count > 0;
	public bool CanImportThing => CanCrop && SelectedTarget?.HasThings == true;
	public bool CanExport => CroppedSprites.Count > 0;

	public SlicerTargetViewModel? SelectedTarget
	{
		get => _selectedTarget;
		set { if (SetProperty(ref _selectedTarget, value)) { RefreshThingChoices(); NotifyCommands(); } }
	}

	public string TemplateSearch { get => _templateSearch; set { if (SetProperty(ref _templateSearch, value)) ApplyChoiceFilters(); } }
	public string ReplacementSearch { get => _replacementSearch; set { if (SetProperty(ref _replacementSearch, value)) ApplyChoiceFilters(); } }
	public WriteableBitmap? TemplatePreview { get => _templatePreview; private set => SetProperty(ref _templatePreview, value); }
	public WriteableBitmap? ReplacementPreview { get => _replacementPreview; private set => SetProperty(ref _replacementPreview, value); }
	public SlicerThingChoiceViewModel? SelectedTemplate
	{
		get => _selectedTemplate;
		set
		{
			if (!SetProperty(ref _selectedTemplate, value)) return;
			if (value != null) TemplateItemId = value.Thing.Id;
			TemplatePreview?.Dispose(); TemplatePreview = value == null ? null : SelectedTarget?.ThingsPanel?.GetPreviewForThing(value.Thing);
		}
	}
	public SlicerThingChoiceViewModel? SelectedReplacement
	{
		get => _selectedReplacement;
		set
		{
			if (!SetProperty(ref _selectedReplacement, value)) return;
			if (value != null) ReplacementThingId = value.Thing.Id;
			ReplacementPreview?.Dispose(); ReplacementPreview = value == null ? null : SelectedTarget?.ThingsPanel?.GetPreviewForThing(value.Thing);
		}
	}

	public void RefreshTargets()
	{
		var previous = SelectedTarget?.SpritePanel ?? _origin;
		Targets.Clear();
		foreach (var sprite in _assets.ActivePanels.OfType<FloatingSpriteLoaderViewModel>().Where(p => p.IsArchiveLoaded))
		{
			var things = _assets.ActivePanels.OfType<FloatingThingsLoaderViewModel>()
				.FirstOrDefault(t => t.IsArchiveLoaded && ReferenceEquals(t.LinkedSpritePanel, sprite));
			Targets.Add(new SlicerTargetViewModel { SpritePanel = sprite, ThingsPanel = things });
		}
		SelectedTarget = Targets.FirstOrDefault(t => ReferenceEquals(t.SpritePanel, previous)) ?? Targets.FirstOrDefault();
	}

	public void LoadImage(string path)
	{
		try
		{
			SetImage(SpritesheetSlicerService.Load(path), keepUndo: false);
			_sourcePath = path;
			_state.LastOpenDirectory = Path.GetDirectoryName(path) ?? "";
			OnPropertyChanged(nameof(SourceFileName));
			Status(false, $"Loaded {SourceFileName} ({ImageWidth}×{ImageHeight}).");
		}
		catch (Exception ex) { Status(true, ex.Message); }
	}

	public void MoveGridTo(int x, int y)
	{
		_offsetX = x; _offsetY = y;
		ClampAndNotifyGrid(forceNotifications: true);
	}

	public void NudgeGrid(int dx, int dy) => MoveGridTo(OffsetX + dx, OffsetY + dy);

	[RelayCommand(CanExecute = nameof(CanCrop))]
	private void Crop()
	{
		if (_image == null) return;
		ClearCropped();
		foreach (var cell in SpritesheetSlicerService.Slice(_image, CurrentGrid(), IncludeEmptySprites))
			CroppedSprites.Add(new SlicerPreviewViewModel
			{
				SourceColumn = cell.Column,
				SourceRow = cell.Row,
				Pixels = cell.Rgba,
				IsEmpty = cell.IsEmpty,
				Preview = _renderer.ConvertRgba(CellSize, CellSize, cell.Rgba)
			});
		Status(false, $"Cropped {CroppedSprites.Count} sprite{(CroppedSprites.Count == 1 ? "" : "s")}.");
		NotifyCommands();
	}

	[RelayCommand]
	private void ClearCropped()
	{
		foreach (var sprite in CroppedSprites) sprite.Preview.Dispose();
		CroppedSprites.Clear(); NotifyCommands();
	}

	[RelayCommand]
	private void RemoveCropped(SlicerPreviewViewModel? sprite)
	{
		if (sprite == null || !CroppedSprites.Remove(sprite)) return;
		sprite.Preview.Dispose(); NotifyCommands();
	}

	[RelayCommand(CanExecute = nameof(CanImportRaw))]
	private void ImportRawSprites()
	{
		try
		{
			var ids = SelectedTarget!.SpritePanel.ImportSlicerSprites(CroppedSprites.Select(s => s.Pixels).ToList());
			Status(false, $"Imported {ids.Count} raw sprite{(ids.Count == 1 ? "" : "s")}.");
		}
		catch (Exception ex) { Status(true, ex.Message); }
	}

	[RelayCommand(CanExecute = nameof(CanImportThing))]
	private void ImportThing()
	{
		try
		{
			var target = SelectedTarget!;
			var thingsPanel = target.ThingsPanel!;
			var allCells = SpritesheetSlicerService.Slice(_image!, CurrentGrid(), includeEmpty: true);
			var template = !ReplaceExisting && IsItem && TemplateItemId > 0
				? thingsPanel.EnumerateThings(ThingKind.Item).FirstOrDefault(t => t.Id == TemplateItemId)
				: null;
			if (!ReplaceExisting && IsItem && TemplateItemId > 0 && template == null)
				throw new InvalidOperationException($"Template item #{TemplateItemId} does not exist.");
			var replacement = ReplaceExisting
				? thingsPanel.EnumerateThings(SelectedKind).FirstOrDefault(t => t.Id == ReplacementThingId)
				: null;
			if (ReplaceExisting && replacement == null)
				throw new InvalidOperationException($"Replacement {SelectedKind.ToString().ToLowerInvariant()} #{ReplacementThingId} does not exist.");

			var request = new SlicerThingBuildRequest(
				SelectedKind, CurrentGrid(), allCells,
				target.SpritePanel.Loader.SpriteCount + 1,
				replacement?.Id ?? thingsPanel.GetNextThingId(SelectedKind),
				ThingWidth, ThingHeight, OutfitDirections, OutfitFrames,
				SettingsViewModel.OutfitAnimationDurationMs,
				thingsPanel.UseFrameAnimations, template, replacement);
			var plan = SpritesheetThingBuilder.Build(request);
			var ids = thingsPanel.ImportSlicerThings(plan.SpritePixels, plan.Things, plan.IsReplacement);
			Status(false, plan.IsReplacement
				? $"Replaced {SelectedKind.ToString().ToLowerInvariant()} #{ids[0]}."
				: $"Created {ids.Count} {SelectedKind.ToString().ToLowerInvariant()}{(ids.Count == 1 ? "" : "s")}.");
			RefreshThingChoices();
		}
		catch (Exception ex) { Status(true, ex.Message); }
	}

	[RelayCommand(CanExecute = nameof(HasImage))]
	private void DetectGrid()
	{
		var result = SpritesheetSlicerService.DetectGrid(_image!, AvailableCellSizes);
		if (!result.Success) { Status(true, result.Message); return; }
		_cellSize = result.Grid.CellSize; _offsetX = result.Grid.X; _offsetY = result.Grid.Y;
		_columns = result.Grid.Columns; _rows = result.Grid.Rows;
		NotifyGridProperties(); Status(false, result.Message);
	}

	[RelayCommand(CanExecute = nameof(HasImage))] private void RotateLeft() => Transform(SpritesheetSlicerService.RotateCounterClockwise);
	[RelayCommand(CanExecute = nameof(HasImage))] private void RotateRight() => Transform(SpritesheetSlicerService.RotateClockwise);
	[RelayCommand(CanExecute = nameof(HasImage))] private void FlipHorizontal() => Transform(SpritesheetSlicerService.FlipHorizontal);
	[RelayCommand(CanExecute = nameof(HasImage))] private void FlipVertical() => Transform(SpritesheetSlicerService.FlipVertical);
	[RelayCommand(CanExecute = nameof(HasImage))] private void MagentaFill() => Transform(SpritesheetSlicerService.FillTransparentWithMagenta);

	[RelayCommand(CanExecute = nameof(CanUndoTransform))]
	private void UndoTransform()
	{
		if (_undoImage == null) return;
		var restore = _undoImage; _undoImage = null; SetImage(restore, keepUndo: true);
		OnPropertyChanged(nameof(CanUndoTransform)); UndoTransformCommand.NotifyCanExecuteChanged();
		Status(false, "Undid the last sheet transform.");
	}

	public IReadOnlyList<string> ExportCropped(string directory, string format = "png")
	{
		if (CroppedSprites.Count == 0) throw new InvalidOperationException("Crop sprites before exporting.");
		var name = string.IsNullOrEmpty(_sourcePath) ? "sprite" : Path.GetFileNameWithoutExtension(_sourcePath);
		var written = CroppedSprites.Select((sprite, i) => SpritesheetSlicerService.ExportImage(sprite.Pixels, CellSize, directory, name, i + 1, format)).ToList();
		_state.LastExportDirectory = directory;
		var label = format.ToLowerInvariant() switch
		{
			"jpg" or "jpeg" => "JPG",
			"bmp" => "BMP",
			_ => "PNG",
		};
		Status(false, $"Exported {written.Count} {label} file{(written.Count == 1 ? "" : "s")} to {directory}.");
		return written;
	}

	public void ReportError(string message) => Status(true, message);

	public PersistenceService.SlicerStateModel CreatePersistentState(bool maximized)
	{
		_state.WasMaximized = maximized; _state.Subdivisions = Subdivisions; _state.IncludeEmptySprites = IncludeEmptySprites;
		_state.ThingWidth = ThingWidth; _state.ThingHeight = ThingHeight; _state.TemplateItemId = TemplateItemId;
		_state.OutfitDirections = OutfitDirections; _state.OutfitFrames = OutfitFrames; _state.ThingKind = SelectedKind.ToString();
		_state.ReplaceExisting = ReplaceExisting;
		return _state;
	}

	private void Transform(Func<SlicerImage, SlicerImage> operation)
	{
		if (_image == null) return;
		_undoImage = _image.Copy(); SetImage(operation(_image), keepUndo: true);
		OnPropertyChanged(nameof(CanUndoTransform)); UndoTransformCommand.NotifyCanExecuteChanged();
		Status(false, "Sheet transform applied in memory.");
	}

	private void SetImage(SlicerImage image, bool keepUndo)
	{
		if (!keepUndo) _undoImage = null;
		_image = image;
		SheetBitmap?.Dispose(); SheetBitmap = _renderer.ConvertRgba(image.Width, image.Height, image.Rgba);
		_columns = Math.Max(1, image.Width / CellSize); _rows = Math.Max(1, image.Height / CellSize); _offsetX = 0; _offsetY = 0;
		ClampAndNotifyGrid(forceNotifications: true); ClearCropped();
		OnPropertyChanged(nameof(Image)); OnPropertyChanged(nameof(HasImage)); OnPropertyChanged(nameof(ImageWidth)); OnPropertyChanged(nameof(ImageHeight));
		NotifyCommands();
	}

	private SlicerGrid CurrentGrid() => SpritesheetSlicerService.ClampGrid(new SlicerGrid(OffsetX, OffsetY, Columns, Rows, CellSize), ImageWidth, ImageHeight);

	private void ClampAndNotifyGrid(bool forceNotifications = false)
	{
		if (_image == null) return;
		var clamped = CurrentGrid();
		var changed = clamped.X != _offsetX || clamped.Y != _offsetY || clamped.Columns != _columns || clamped.Rows != _rows || clamped.CellSize != _cellSize;
		_offsetX = clamped.X; _offsetY = clamped.Y; _columns = clamped.Columns; _rows = clamped.Rows; _cellSize = clamped.CellSize;
		if (changed || forceNotifications) NotifyGridProperties();
	}

	private void NotifyGridProperties()
	{
		OnPropertyChanged(nameof(OffsetX)); OnPropertyChanged(nameof(OffsetY)); OnPropertyChanged(nameof(Columns)); OnPropertyChanged(nameof(Rows)); OnPropertyChanged(nameof(CellSize));
		NotifyValidation();
	}

	private void NotifyValidation()
	{
		OnPropertyChanged(nameof(IsOutfitLayoutValid)); OnPropertyChanged(nameof(OutfitLayoutHint)); OnPropertyChanged(nameof(SplitHint)); NotifyCommands();
	}

	private void NotifyCommands()
	{
		OnPropertyChanged(nameof(CanCrop)); OnPropertyChanged(nameof(CanImportRaw)); OnPropertyChanged(nameof(CanImportThing)); OnPropertyChanged(nameof(CanExport));
		CropCommand.NotifyCanExecuteChanged(); DetectGridCommand.NotifyCanExecuteChanged(); ImportRawSpritesCommand.NotifyCanExecuteChanged(); ImportThingCommand.NotifyCanExecuteChanged();
		RotateLeftCommand.NotifyCanExecuteChanged(); RotateRightCommand.NotifyCanExecuteChanged(); FlipHorizontalCommand.NotifyCanExecuteChanged(); FlipVerticalCommand.NotifyCanExecuteChanged(); MagentaFillCommand.NotifyCanExecuteChanged();
	}

	private void RefreshThingChoices()
	{
		_allTemplates.Clear(); _allReplacements.Clear();
		var things = SelectedTarget?.ThingsPanel;
		if (things != null)
		{
			foreach (var thing in things.EnumerateThings(ThingKind.Item))
				_allTemplates.Add(new SlicerThingChoiceViewModel { Thing = thing });
			foreach (var thing in things.EnumerateThings(SelectedKind))
				_allReplacements.Add(new SlicerThingChoiceViewModel { Thing = thing });
		}
		ApplyChoiceFilters();
		SelectedTemplate = _allTemplates.FirstOrDefault(c => c.Thing.Id == TemplateItemId);
		SelectedReplacement = _allReplacements.FirstOrDefault(c => c.Thing.Id == ReplacementThingId);
	}

	private void ApplyChoiceFilters()
	{
		ReplaceCollection(TemplateChoices, _allTemplates.Where(c => Matches(c, TemplateSearch)).Take(200));
		ReplaceCollection(ReplacementChoices, _allReplacements.Where(c => Matches(c, ReplacementSearch)).Take(200));
	}

	private static bool Matches(SlicerThingChoiceViewModel choice, string search) => string.IsNullOrWhiteSpace(search) || choice.DisplayName.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase);
	private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> source) { target.Clear(); foreach (var item in source) target.Add(item); }

	private void Status(bool error, string message) { StatusIsError = error; StatusMessage = message; }
	private void OnPanelsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshTargets();

	public void Dispose()
	{
		_assets.ActivePanels.CollectionChanged -= OnPanelsChanged;
		SheetBitmap?.Dispose(); ClearCropped();
		TemplatePreview?.Dispose(); ReplacementPreview?.Dispose();
	}
}
