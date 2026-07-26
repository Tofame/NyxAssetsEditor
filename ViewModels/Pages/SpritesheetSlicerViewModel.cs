using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using NyxAssets.Things;
using NyxAssetsEditor.Models;
using NyxAssetsEditor.Services.ImportExport;
using NyxAssetsEditor.Services.Persistence;
using NyxAssetsEditor.Services.Rendering;
using NyxAssetsEditor.Services.Things;
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

public partial class SpritesheetSlicerViewModel : ViewModelBase, IDisposable, IThingFinderContextActionProvider
{
	private readonly AssetsViewModel _assets;
	private readonly SpriteRenderer _renderer = new();
	private readonly FloatingSpriteLoaderViewModel? _origin;
	private readonly PersistenceService.SlicerStateModel _state;
	private SlicerImage? _image;
	private SlicerImage? _undoImage;
	private SlicerGrid? _undoGrid;
	private int _imageRevision;
	private int _lastCropRevision = -1;
	private SlicerGrid? _lastCropGrid;
	private WriteableBitmap? _sheetBitmap;
	private string _sourcePath = "";
	private int _offsetX;
	private int _offsetY;
	private int _columns = 1;
	private int _rows = 1;
	private int _cellSize = SpriteModel.SpriteSize;
	private bool _autoDetectSpriteGrid = true;
	private bool _applyingAutomaticGrid;
	private double _zoom = 1;
	private int _thingWidth;
	private int _thingHeight;
	private int _outfitDirections = 4;
	private int _outfitFrames = 3;
	private uint _templateThingId;
	private uint _replacementThingId;
	private bool _useTemplate;
	private bool _replaceExisting;
	private ThingKind _selectedKind = ThingKind.Item;
	private string _statusMessage = "Open or drop an image to begin.";
	private bool _statusIsError;
	private SlicerTargetViewModel? _selectedTarget;
	private SlicerThingChoiceViewModel? _selectedTemplate;
	private SlicerThingChoiceViewModel? _selectedReplacement;
	private WriteableBitmap? _templatePreview;
	private WriteableBitmap? _replacementPreview;
	private readonly List<SlicerThingChoiceViewModel> _allTemplates = new();
	private readonly List<SlicerThingChoiceViewModel> _allReplacements = new();
	private bool _targetsInitialized;

	public SpritesheetSlicerViewModel(AssetsViewModel assets, FloatingSpriteLoaderViewModel? origin = null)
	{
		_assets = assets;
		_origin = origin ?? assets.LastActivePair?.SpritePanel;
		_state = PersistenceService.GetSlicerState();
		_autoDetectSpriteGrid = _state.AutoDetectSpriteGrid;
		_thingWidth = Math.Max(0, _state.ThingWidth);
		_thingHeight = Math.Max(0, _state.ThingHeight);
		_outfitDirections = Math.Max(1, _state.OutfitDirections);
		_outfitFrames = Math.Max(1, _state.OutfitFrames);
		_replaceExisting = _state.ReplaceExisting;
		if (Enum.TryParse<ThingKind>(_state.ThingKind, out var kind)) _selectedKind = kind;
		_assets.ActivePanels.CollectionChanged += OnPanelsChanged;
		_assets.RegisterThingFinderContextActionProvider(this);
		RefreshTargets();
	}

	public ObservableCollection<SlicerTargetViewModel> Targets { get; } = new();
	public ObservableCollection<SlicerPreviewViewModel> CroppedSprites { get; } = new();
	public IReadOnlyList<int> AvailableCellSizes { get; } = new[] { SpriteModel.SpriteSize };
	public IReadOnlyList<double> AvailableZoomLevels { get; } = new[] { 1d, 2d, 4d, 8d, 16d };
	public IReadOnlyList<ThingKind> ThingKinds { get; } = new[] { ThingKind.Item, ThingKind.Outfit, ThingKind.Effect, ThingKind.Missile };

	public SlicerImage? Image => _image;
	public WriteableBitmap? SheetBitmap { get => _sheetBitmap; private set => SetProperty(ref _sheetBitmap, value); }
	public bool HasImage => _image != null;
	public int ImageWidth => _image?.Width ?? 0;
	public int ImageHeight => _image?.Height ?? 0;
	public string SourceFileName => string.IsNullOrEmpty(_sourcePath) ? "No image loaded" : Path.GetFileName(_sourcePath);
	public string LastOpenDirectory => _state.LastOpenDirectory;
	public string LastExportDirectory => _state.LastExportDirectory;

	public int OffsetX { get => _offsetX; set { if (SetProperty(ref _offsetX, value)) ManualGridChanged(); } }
	public int OffsetY { get => _offsetY; set { if (SetProperty(ref _offsetY, value)) ManualGridChanged(); } }
	public int Columns { get => _columns; set { if (SetProperty(ref _columns, value)) ManualGridChanged(); } }
	public int Rows { get => _rows; set { if (SetProperty(ref _rows, value)) ManualGridChanged(); } }
	public int CellSize
	{
		get => _cellSize;
		set
		{
			if (!SetProperty(ref _cellSize, value)) return;
			if (AutoDetectSpriteGrid) ApplyAutomaticGrid(showStatus: true);
			else ClampAndNotifyGrid();
		}
	}
	public bool AutoDetectSpriteGrid
	{
		get => _autoDetectSpriteGrid;
		set
		{
			if (!SetProperty(ref _autoDetectSpriteGrid, value)) return;
			if (value) ApplyAutomaticGrid(showStatus: true);
		}
	}
	public double Zoom { get => _zoom; set => SetProperty(ref _zoom, SnapZoom(value)); }
	public int ThingWidth { get => _thingWidth; set { if (SetProperty(ref _thingWidth, Math.Max(0, value))) NotifyValidation(); } }
	public int ThingHeight { get => _thingHeight; set { if (SetProperty(ref _thingHeight, Math.Max(0, value))) NotifyValidation(); } }
	public int OutfitDirections { get => _outfitDirections; set { if (SetProperty(ref _outfitDirections, Math.Max(1, value))) NotifyValidation(); } }
	public int OutfitFrames { get => _outfitFrames; set { if (SetProperty(ref _outfitFrames, Math.Max(1, value))) NotifyValidation(); } }
	public uint TemplateThingId
	{
		get => _templateThingId;
		set
		{
			if (!SetProperty(ref _templateThingId, value)) return;
			if (SelectedTemplate?.Thing.Id != value) SelectedTemplate = _allTemplates.FirstOrDefault(c => c.Thing.Id == value);
			OnPropertyChanged(nameof(TemplateSelectionLabel));
			NotifyValidation();
		}
	}
	public uint ReplacementThingId
	{
		get => _replacementThingId;
		set
		{
			if (!SetProperty(ref _replacementThingId, value)) return;
			if (SelectedReplacement?.Thing.Id != value) SelectedReplacement = _allReplacements.FirstOrDefault(c => c.Thing.Id == value);
			OnPropertyChanged(nameof(ReplacementSelectionLabel));
			NotifyValidation();
		}
	}
	public bool UseTemplate
	{
		get => _useTemplate;
		set
		{
			if (!SetProperty(ref _useTemplate, value)) return;
			if (!value)
			{
				_templateThingId = 0;
				OnPropertyChanged(nameof(TemplateThingId));
				SelectedTemplate = null;
			}
			OnPropertyChanged(nameof(ShowTemplatePicker)); OnPropertyChanged(nameof(CanChooseTemplate));
			NotifyValidation();
		}
	}

	public bool ReplaceExisting
	{
		get => _replaceExisting;
		set
		{
			if (!SetProperty(ref _replaceExisting, value)) return;
			if (value) UseTemplate = false;
			OnPropertyChanged(nameof(IsCreateMode)); OnPropertyChanged(nameof(ShowTemplatePicker));
			OnPropertyChanged(nameof(CanChooseTemplate)); OnPropertyChanged(nameof(CanChooseReplacement));
			NotifyValidation();
		}
	}
	public bool IsCreateMode => !ReplaceExisting;

	public ThingKind SelectedKind
	{
		get => _selectedKind;
		set
		{
			if (!SetProperty(ref _selectedKind, value)) return;
			TemplateThingId = 0;
			OnPropertyChanged(nameof(IsItem)); OnPropertyChanged(nameof(IsOutfit)); OnPropertyChanged(nameof(UsesFootprint));
			OnPropertyChanged(nameof(ShowTemplatePicker)); OnPropertyChanged(nameof(CanChooseTemplate));
			OnPropertyChanged(nameof(ReplacementSelectionLabel));
			RefreshThingChoices();
			if (IsOutfit) InitializeOutfitGridIfUntouched();
			NotifyValidation();
		}
	}

	public bool IsItem => SelectedKind == ThingKind.Item;
	public bool IsOutfit => SelectedKind == ThingKind.Outfit;
	public bool UsesFootprint => SelectedKind != ThingKind.Outfit;
	public bool ShowTemplatePicker => IsCreateMode && UseTemplate;
	public bool CanChooseTemplate => ShowTemplatePicker && SelectedTarget?.HasThings == true;
	public bool CanChooseReplacement => ReplaceExisting && SelectedTarget?.HasThings == true;
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
	public bool CanImportThing => CanCrop && SelectedTarget?.HasThings == true &&
		(!ReplaceExisting || SelectedReplacement != null) &&
		(ReplaceExisting || !UseTemplate || SelectedTemplate != null);
	public bool CanExport => CroppedSprites.Count > 0;

	public SlicerTargetViewModel? SelectedTarget
	{
		get => _selectedTarget;
		set { if (SetProperty(ref _selectedTarget, value)) { RefreshThingChoices(); NotifyCommands(); } }
	}

	public string TemplateSelectionLabel => SelectedTemplate?.DisplayName ?? (TemplateThingId == 0 ? "No template selected" : $"{SelectedKind} #{TemplateThingId} was not found");
	public string ReplacementSelectionLabel => SelectedReplacement?.DisplayName ?? (ReplacementThingId == 0 ? "No thing selected" : $"{SelectedKind} #{ReplacementThingId} was not found");
	public WriteableBitmap? TemplatePreview { get => _templatePreview; private set => SetProperty(ref _templatePreview, value); }
	public WriteableBitmap? ReplacementPreview { get => _replacementPreview; private set => SetProperty(ref _replacementPreview, value); }
	public SlicerThingChoiceViewModel? SelectedTemplate
	{
		get => _selectedTemplate;
		set
		{
			if (!SetProperty(ref _selectedTemplate, value)) return;
			if (value != null) TemplateThingId = value.Thing.Id;
			TemplatePreview?.Dispose(); TemplatePreview = value == null ? null : SelectedTarget?.ThingsPanel?.GetPreviewForThing(value.Thing);
			OnPropertyChanged(nameof(TemplateSelectionLabel));
			NotifyValidation();
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
			OnPropertyChanged(nameof(ReplacementSelectionLabel));
			NotifyValidation();
		}
	}

	public void RefreshTargets()
	{
		var previous = SelectedTarget?.SpritePanel;
		Targets.Clear();
		foreach (var sprite in _assets.ActivePanels.OfType<FloatingSpriteLoaderViewModel>().Where(p => p.IsArchiveLoaded))
		{
			var things = _assets.ActivePanels.OfType<FloatingThingsLoaderViewModel>()
				.FirstOrDefault(t => t.IsArchiveLoaded && ReferenceEquals(t.LinkedSpritePanel, sprite));
			Targets.Add(new SlicerTargetViewModel { SpritePanel = sprite, ThingsPanel = things });
		}
		if (!_targetsInitialized)
		{
			_targetsInitialized = true;
			SelectedTarget = Targets.FirstOrDefault(t => ReferenceEquals(t.SpritePanel, _origin)) ?? Targets.FirstOrDefault();
			return;
		}

		var restored = previous == null ? null : Targets.FirstOrDefault(t => ReferenceEquals(t.SpritePanel, previous));
		SelectedTarget = restored;
		if (previous != null && restored == null)
			Status(true, "The selected import target was closed. Choose another target before importing.");
	}

	public void LoadImage(string path)
	{
		try
		{
			_undoImage = null;
			_undoGrid = null;
			ApplyImage(SpritesheetSlicerService.Load(path), resetGrid: true, clearCropped: true);
			_sourcePath = path;
			_state.LastOpenDirectory = Path.GetDirectoryName(path) ?? "";
			OnPropertyChanged(nameof(SourceFileName));
			Status(false, $"Loaded {SourceFileName} ({ImageWidth}×{ImageHeight}).");
		}
		catch (Exception ex) { Status(true, ex.Message); }
	}

	public void MoveGridTo(int x, int y)
	{
		if (!_applyingAutomaticGrid) AutoDetectSpriteGrid = false;
		_offsetX = x; _offsetY = y;
		ClampAndNotifyGrid(forceNotifications: true);
	}

	public void NudgeGrid(int dx, int dy) => MoveGridTo(OffsetX + dx, OffsetY + dy);
	public void ZoomIn() => Zoom = AvailableZoomLevels.FirstOrDefault(level => level > Zoom, AvailableZoomLevels[^1]);
	public void ZoomOut() => Zoom = AvailableZoomLevels.LastOrDefault(level => level < Zoom, AvailableZoomLevels[0]);

	[RelayCommand(CanExecute = nameof(CanCrop))]
	private void Crop()
	{
		if (_image == null) return;
		var grid = CurrentGrid();
		if (CroppedSprites.Count > 0 && _lastCropRevision == _imageRevision && _lastCropGrid == grid)
		{
			Status(false, "This selection is already in the crop list. Move the grid or clear the list to crop it again.");
			return;
		}

		var added = 0;
		foreach (var cell in SpritesheetSlicerService.Slice(_image, grid, includeEmpty: false))
		{
			CroppedSprites.Add(new SlicerPreviewViewModel
			{
				SourceColumn = cell.Column,
				SourceRow = cell.Row,
				Pixels = cell.Rgba,
				IsEmpty = cell.IsEmpty,
				Preview = _renderer.ConvertRgba(CellSize, CellSize, cell.Rgba)
			});
			added++;
		}
		_lastCropRevision = _imageRevision;
		_lastCropGrid = grid;
		Status(false, $"Added {added} sprite{(added == 1 ? "" : "s")} ({CroppedSprites.Count} total).");
		NotifyCommands();
	}

	[RelayCommand]
	private void ClearCropped()
	{
		foreach (var sprite in CroppedSprites) sprite.Preview.Dispose();
		CroppedSprites.Clear();
		_lastCropRevision = -1;
		_lastCropGrid = null;
		NotifyCommands();
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
			var template = !ReplaceExisting && UseTemplate && TemplateThingId > 0
				? thingsPanel.EnumerateThings(SelectedKind).FirstOrDefault(t => t.Id == TemplateThingId)
				: null;
			if (!ReplaceExisting && UseTemplate && template == null)
				throw new InvalidOperationException($"Template {SelectedKind.ToString().ToLowerInvariant()} #{TemplateThingId} does not exist.");
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

	[RelayCommand(CanExecute = nameof(CanChooseTemplate))]
	private void FindTemplate()
	{
		if (SelectedTarget?.ThingsPanel is { } things) _assets.OpenThingFinder(things, SelectedKind);
	}

	[RelayCommand]
	private void ClearTemplate() => TemplateThingId = 0;

	[RelayCommand(CanExecute = nameof(CanChooseReplacement))]
	private void FindReplacement()
	{
		if (SelectedTarget?.ThingsPanel is { } things) _assets.OpenThingFinder(things, SelectedKind);
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
		var restore = _undoImage;
		var restoreGrid = _undoGrid;
		_undoImage = null;
		_undoGrid = null;
		ApplyImage(restore, resetGrid: false, clearCropped: false);
		if (restoreGrid is { } grid)
		{
			_offsetX = grid.X; _offsetY = grid.Y; _columns = grid.Columns; _rows = grid.Rows; _cellSize = grid.CellSize;
			ClampAndNotifyGrid(forceNotifications: true);
		}
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
		_state.WasMaximized = maximized;
		_state.AutoDetectSpriteGrid = AutoDetectSpriteGrid;
		_state.ThingWidth = ThingWidth; _state.ThingHeight = ThingHeight;
		_state.OutfitDirections = OutfitDirections; _state.OutfitFrames = OutfitFrames; _state.ThingKind = SelectedKind.ToString();
		_state.ReplaceExisting = ReplaceExisting;
		return _state;
	}

	private void Transform(Func<SlicerImage, SlicerImage> operation)
	{
		if (_image == null) return;
		_undoImage = _image.Copy();
		_undoGrid = CurrentGrid();
		var transformed = operation(_image);
		var dimensionsChanged = transformed.Width != _image.Width || transformed.Height != _image.Height;
		ApplyImage(transformed, resetGrid: dimensionsChanged, clearCropped: false);
		OnPropertyChanged(nameof(CanUndoTransform)); UndoTransformCommand.NotifyCanExecuteChanged();
		Status(false, "Sheet transform applied in memory.");
	}

	private void ApplyImage(SlicerImage image, bool resetGrid, bool clearCropped)
	{
		_image = image;
		_imageRevision++;
		SheetBitmap?.Dispose(); SheetBitmap = _renderer.ConvertRgba(image.Width, image.Height, image.Rgba);
		if (resetGrid)
		{
			_columns = 1; _rows = 1; _offsetX = 0; _offsetY = 0;
			Zoom = SpritesheetSlicerService.RecommendZoom(image.Width, image.Height);
		}
		ClampAndNotifyGrid(forceNotifications: true);
		if (resetGrid && AutoDetectSpriteGrid) ApplyAutomaticGrid(showStatus: false);
		else if (resetGrid && IsOutfit) InitializeOutfitGridIfUntouched();
		if (clearCropped) ClearCropped();
		OnPropertyChanged(nameof(Image)); OnPropertyChanged(nameof(HasImage)); OnPropertyChanged(nameof(ImageWidth)); OnPropertyChanged(nameof(ImageHeight));
		NotifyCommands();
	}

	private SlicerGrid CurrentGrid() => SpritesheetSlicerService.ClampGrid(new SlicerGrid(OffsetX, OffsetY, Columns, Rows, CellSize), ImageWidth, ImageHeight);

	private double SnapZoom(double requested)
	{
		var rounded = Math.Round(Math.Clamp(requested, AvailableZoomLevels[0], AvailableZoomLevels[^1]), 1);
		var preset = AvailableZoomLevels.OrderBy(level => Math.Abs(level - rounded)).First();
		return Math.Abs(preset - rounded) <= 0.15 ? preset : rounded;
	}

	private void ManualGridChanged()
	{
		if (!_applyingAutomaticGrid) AutoDetectSpriteGrid = false;
		ClampAndNotifyGrid();
	}

	private void ApplyAutomaticGrid(bool showStatus)
	{
		if (_image == null || CellSize <= 0) return;
		_applyingAutomaticGrid = true;
		try
		{
			var detected = SpritesheetSlicerService.DetectGrid(_image, new[] { CellSize });
			if (detected.Success)
			{
				_offsetX = detected.Grid.X; _offsetY = detected.Grid.Y;
				_columns = detected.Grid.Columns; _rows = detected.Grid.Rows;
				ClampAndNotifyGrid(forceNotifications: true);
				if (showStatus) Status(false, "Detected the sprite grid from transparent separators. Review it before cropping.");
				return;
			}

			_offsetX = 0; _offsetY = 0;
			_columns = Math.Max(1, _image.Width / CellSize);
			_rows = Math.Max(1, _image.Height / CellSize);
			ClampAndNotifyGrid(forceNotifications: true);
			if (showStatus) Status(false, $"Fitted a {_columns}×{_rows} grid using the {CellSize}×{CellSize} project sprite size.");
		}
		finally { _applyingAutomaticGrid = false; }
	}

	private void InitializeOutfitGridIfUntouched()
	{
		if (_image == null || _columns != 1 || _rows != 1) return;
		if (_image.Width < OutfitDirections * CellSize || _image.Height < OutfitFrames * CellSize) return;
		_columns = OutfitDirections;
		_rows = OutfitFrames;
		ClampAndNotifyGrid(forceNotifications: true);
	}

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
		OnPropertyChanged(nameof(SplitHint)); NotifyCommands();
	}

	private void NotifyCommands()
	{
		OnPropertyChanged(nameof(CanCrop)); OnPropertyChanged(nameof(CanImportRaw)); OnPropertyChanged(nameof(CanImportThing)); OnPropertyChanged(nameof(CanExport));
		CropCommand.NotifyCanExecuteChanged(); ImportRawSpritesCommand.NotifyCanExecuteChanged(); ImportThingCommand.NotifyCanExecuteChanged();
		RotateLeftCommand.NotifyCanExecuteChanged(); RotateRightCommand.NotifyCanExecuteChanged(); FlipHorizontalCommand.NotifyCanExecuteChanged(); FlipVerticalCommand.NotifyCanExecuteChanged(); MagentaFillCommand.NotifyCanExecuteChanged();
		FindTemplateCommand.NotifyCanExecuteChanged(); FindReplacementCommand.NotifyCanExecuteChanged();
		OnPropertyChanged(nameof(CanUndoTransform)); UndoTransformCommand.NotifyCanExecuteChanged();
	}

	private void RefreshThingChoices()
	{
		_allTemplates.Clear(); _allReplacements.Clear();
		var things = SelectedTarget?.ThingsPanel;
		if (things != null)
		{
			foreach (var thing in things.EnumerateThings(SelectedKind))
				_allTemplates.Add(new SlicerThingChoiceViewModel { Thing = thing });
			foreach (var thing in things.EnumerateThings(SelectedKind))
				_allReplacements.Add(new SlicerThingChoiceViewModel { Thing = thing });
		}
		SelectedTemplate = _allTemplates.FirstOrDefault(c => c.Thing.Id == TemplateThingId);
		SelectedReplacement = _allReplacements.FirstOrDefault(c => c.Thing.Id == ReplacementThingId);
	}

	public IEnumerable<ThingFinderContextAction> GetThingFinderContextActions(FloatingThingsLoaderViewModel source, ThingType thing)
	{
		if (!ReferenceEquals(SelectedTarget?.ThingsPanel, source)) yield break;
		if (ShowTemplatePicker && thing.Kind == SelectedKind)
		{
			yield return new ThingFinderContextAction("Use as slicer template", () =>
			{
				TemplateThingId = thing.Id;
				return Task.CompletedTask;
			});
		}
		if (ReplaceExisting && thing.Kind == SelectedKind)
		{
			yield return new ThingFinderContextAction("Replace this thing in slicer", () =>
			{
				ReplacementThingId = thing.Id;
				return Task.CompletedTask;
			});
		}
	}

	private void Status(bool error, string message) { StatusIsError = error; StatusMessage = message; }
	private void OnPanelsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshTargets();

	public void Dispose()
	{
		_assets.ActivePanels.CollectionChanged -= OnPanelsChanged;
		_assets.UnregisterThingFinderContextActionProvider(this);
		SheetBitmap?.Dispose(); ClearCropped();
		TemplatePreview?.Dispose(); ReplacementPreview?.Dispose();
	}
}
