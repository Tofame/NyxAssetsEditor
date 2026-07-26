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
	public required int ExportIndex { get; init; }
	public required byte[] Pixels { get; init; }
	public required bool IsEmpty { get; init; }
	public required WriteableBitmap Preview { get; init; }
	public string Label => $"#{ExportIndex:0000}  ({SourceColumn + 1}, {SourceRow + 1})" + (IsEmpty ? " empty" : "");
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
	private double _zoom = 1;
	private int _thingWidth;
	private int _thingHeight;
	private int _thingLayers = 1;
	private int _thingPatternX = 1;
	private int _thingPatternY = 1;
	private int _thingPatternZ = 1;
	private int _thingFrames = 1;
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
		_thingWidth = Math.Max(0, _state.ThingWidth);
		_thingHeight = Math.Max(0, _state.ThingHeight);
		_thingLayers = Math.Max(1, _state.ThingLayers);
		_thingPatternX = Math.Max(1, _state.ThingPatternX);
		_thingPatternY = Math.Max(1, _state.ThingPatternY);
		_thingPatternZ = Math.Max(1, _state.ThingPatternZ);
		_thingFrames = Math.Max(1, _state.ThingFrames);
		_outfitDirections = Math.Max(1, _state.OutfitDirections);
		_outfitFrames = Math.Max(1, _state.OutfitFrames);
		_replaceExisting = _state.ReplaceExisting;
		if (Enum.TryParse<ThingKind>(_state.ThingKind, out var kind)) _selectedKind = kind;
		if (_selectedKind == ThingKind.Missile && _thingPatternX == 1 && _thingPatternY == 1)
		{
			_thingPatternX = 3;
			_thingPatternY = 3;
		}
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
			FitGridToImage(showStatus: true);
		}
	}
	public double Zoom { get => _zoom; set => SetProperty(ref _zoom, SnapZoom(value)); }
	public int ThingWidth { get => _thingWidth; set { if (SetProperty(ref _thingWidth, Math.Max(0, value))) NotifyValidation(); } }
	public int ThingHeight { get => _thingHeight; set { if (SetProperty(ref _thingHeight, Math.Max(0, value))) NotifyValidation(); } }
	public int ThingLayers
	{
		get => _thingLayers;
		set
		{
			if (!SetProperty(ref _thingLayers, Math.Max(1, value))) return;
			OnPropertyChanged(nameof(OutfitHasRecolourMask));
			NotifyValidation();
		}
	}
	public int ThingPatternX { get => _thingPatternX; set { if (SetProperty(ref _thingPatternX, Math.Max(1, value))) NotifyValidation(); } }
	public int ThingPatternY
	{
		get => _thingPatternY;
		set
		{
			if (!SetProperty(ref _thingPatternY, Math.Max(1, value))) return;
			OnPropertyChanged(nameof(OutfitAddonCount));
			NotifyValidation();
		}
	}
	public int ThingPatternZ
	{
		get => _thingPatternZ;
		set
		{
			if (!SetProperty(ref _thingPatternZ, Math.Max(1, value))) return;
			OnPropertyChanged(nameof(OutfitHasMountedPose));
			NotifyValidation();
		}
	}
	public int ThingFrames { get => _thingFrames; set { if (SetProperty(ref _thingFrames, Math.Max(1, value))) NotifyValidation(); } }
	public int OutfitDirections { get => _outfitDirections; set { if (SetProperty(ref _outfitDirections, Math.Max(1, value))) NotifyValidation(); } }
	public int OutfitFrames { get => _outfitFrames; set { if (SetProperty(ref _outfitFrames, Math.Max(1, value))) NotifyValidation(); } }
	public bool OutfitHasRecolourMask
	{
		get => ThingLayers >= 2;
		set => ThingLayers = value ? 2 : 1;
	}
	public int OutfitAddonCount
	{
		get => Math.Max(0, ThingPatternY - 1);
		set => ThingPatternY = checked(Math.Max(0, value) + 1);
	}
	public bool OutfitHasMountedPose
	{
		get => ThingPatternZ >= 2;
		set => ThingPatternZ = value ? 2 : 1;
	}
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
			OnPropertyChanged(nameof(IsItem)); OnPropertyChanged(nameof(IsOutfit)); OnPropertyChanged(nameof(IsMissile));
			OnPropertyChanged(nameof(ShowTemplatePicker)); OnPropertyChanged(nameof(CanChooseTemplate));
			OnPropertyChanged(nameof(ReplacementSelectionLabel));
			RefreshThingChoices();
			if (IsMissile && ThingPatternX == 1 && ThingPatternY == 1)
			{
				_thingPatternX = 3;
				_thingPatternY = 3;
				OnPropertyChanged(nameof(ThingPatternX));
				OnPropertyChanged(nameof(ThingPatternY));
			}
			NotifyValidation();
		}
	}

	public bool IsItem => SelectedKind == ThingKind.Item;
	public bool IsOutfit => SelectedKind == ThingKind.Outfit;
	public bool IsMissile => SelectedKind == ThingKind.Missile;
	public bool UsesCombinedLayout => TryGetCombinedLayoutDimensions(out _, out _);
	public string OutfitFrameGroupHint => SelectedTarget?.ThingsPanel switch
	{
		null => "Choose a target to resolve idle and walking frames.",
		{ UseFrameGroups: true } when OutfitFrames >= 3 && !UsesCombinedLayout =>
			"Frame 1 = idle; remaining frames = walking.",
		{ UseFrameGroups: true } => "Idle and walking use separate frame groups.",
		_ => "Idle and walking share one legacy frame group."
	};
	public bool ShowTemplatePicker => IsCreateMode && UseTemplate;
	public bool CanChooseTemplate => ShowTemplatePicker && SelectedTarget?.HasThings == true;
	public bool CanChooseReplacement => ReplaceExisting && SelectedTarget?.HasThings == true;
	public string SplitHint => GetLayoutStatus().Message;

	public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
	public bool StatusIsError { get => _statusIsError; private set => SetProperty(ref _statusIsError, value); }
	public bool CanUndoTransform => _undoImage != null;
	public bool CanCrop => HasImage && Columns > 0 && Rows > 0;
	public bool CanImportRaw => SelectedTarget?.SpritePanel.IsArchiveLoaded == true && CroppedSprites.Count > 0;
	public bool CanImportThing => CanCrop && GetLayoutStatus().Valid && SelectedTarget?.HasThings == true &&
		(!ReplaceExisting || SelectedReplacement != null) &&
		(ReplaceExisting || !UseTemplate || SelectedTemplate != null);
	public bool CanExport => CroppedSprites.Count > 0;

	public SlicerTargetViewModel? SelectedTarget
	{
		get => _selectedTarget;
		set { if (SetProperty(ref _selectedTarget, value)) { RefreshThingChoices(); OnPropertyChanged(nameof(OutfitFrameGroupHint)); NotifyCommands(); } }
	}

	public string TemplateSelectionLabel => SelectedTemplate is { } template
		? DescribeThingSelection(template, replacing: false)
		: TemplateThingId == 0 ? "No template selected" : $"{SelectedKind} #{TemplateThingId} was not found";
	public string ReplacementSelectionLabel => SelectedReplacement is { } replacement
		? DescribeThingSelection(replacement, replacing: true)
		: ReplacementThingId == 0 ? "No thing selected" : $"{SelectedKind} #{ReplacementThingId} was not found";
	public WriteableBitmap? TemplatePreview { get => _templatePreview; private set => SetProperty(ref _templatePreview, value); }
	public WriteableBitmap? ReplacementPreview { get => _replacementPreview; private set => SetProperty(ref _replacementPreview, value); }
	public SlicerThingChoiceViewModel? SelectedTemplate
	{
		get => _selectedTemplate;
		set
		{
			var previousThing = _selectedTemplate?.Thing;
			if (!SetProperty(ref _selectedTemplate, value)) return;
			if (value != null)
			{
				TemplateThingId = value.Thing.Id;
				if (!ReferenceEquals(previousThing, value.Thing)) ApplyLayoutFromThing(value.Thing);
			}
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
			var previousThing = _selectedReplacement?.Thing;
			if (!SetProperty(ref _selectedReplacement, value)) return;
			if (value != null)
			{
				ReplacementThingId = value.Thing.Id;
				if (!ReferenceEquals(previousThing, value.Thing)) ApplyLayoutFromThing(value.Thing);
			}
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
			var loaded = SpritesheetSlicerService.Load(path);
			_sourcePath = path;
			_state.LastOpenDirectory = Path.GetDirectoryName(path) ?? "";
			ApplyImage(loaded, resetGrid: true, clearCropped: true);
			OnPropertyChanged(nameof(SourceFileName));
			if (loaded.Width % CellSize == 0 && loaded.Height % CellSize == 0)
				Status(false, $"Loaded {SourceFileName} ({ImageWidth}×{ImageHeight}); selected the complete sprite grid.");
			else if (loaded.Width % CellSize != 0 || loaded.Height % CellSize != 0)
				Status(true, $"Loaded {SourceFileName} ({ImageWidth}×{ImageHeight}), but it is not an exact multiple of {CellSize}×{CellSize}. Align the grid manually.");
			else
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
				// Object Builder's raw slicer walks down each column before moving right.
				// Keep that original slot number even when transparent cells are omitted.
				ExportIndex = cell.Column * grid.Rows + cell.Row + 1,
				Pixels = cell.Rgba,
				IsEmpty = cell.IsEmpty,
				Preview = _renderer.ConvertRgba(CellSize, CellSize, cell.Rgba)
			});
			added++;
		}
		_lastCropRevision = _imageRevision;
		_lastCropGrid = grid;
		var emptySlots = grid.Columns * grid.Rows - added;
		Status(false, emptySlots > 0
			? $"Added {added} sprite{(added == 1 ? "" : "s")}; preserved {emptySlots} empty slot number{(emptySlots == 1 ? "" : "s")} ({CroppedSprites.Count} total)."
			: $"Added {added} sprite{(added == 1 ? "" : "s")} ({CroppedSprites.Count} total).");
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
				ThingWidth, ThingHeight, ThingLayers,
				IsOutfit ? OutfitDirections : ThingPatternX,
				ThingPatternY, ThingPatternZ,
				IsOutfit ? OutfitFrames : ThingFrames,
				GetAnimationDuration(),
				thingsPanel.UseFrameAnimations, template, replacement,
				thingsPanel.UseFrameGroups);
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
		var written = CroppedSprites.Select(sprite => SpritesheetSlicerService.ExportImage(
			sprite.Pixels, CellSize, directory, name, sprite.ExportIndex, format)).ToList();
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
		_state.ThingWidth = ThingWidth; _state.ThingHeight = ThingHeight;
		_state.ThingLayers = ThingLayers; _state.ThingPatternX = ThingPatternX;
		_state.ThingPatternY = ThingPatternY; _state.ThingPatternZ = ThingPatternZ; _state.ThingFrames = ThingFrames;
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
		if (resetGrid) FitGridToImage(showStatus: false);
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
		ClampAndNotifyGrid();
	}

	private void FitGridToImage(bool showStatus)
	{
		if (_image == null || CellSize <= 0) return;
		var detected = SpritesheetSlicerService.DetectGrid(_image, new[] { CellSize });
		if (detected.Success)
		{
			_offsetX = detected.Grid.X; _offsetY = detected.Grid.Y;
			_columns = detected.Grid.Columns; _rows = detected.Grid.Rows;
			ClampAndNotifyGrid(forceNotifications: true);
			if (showStatus) Status(false, detected.Message);
			return;
		}

		ClampAndNotifyGrid(forceNotifications: true);
		if (showStatus) Status(true, detected.Message);
	}

	private uint GetAnimationDuration() => SelectedKind switch
	{
		ThingKind.Item => SettingsViewModel.ItemAnimationDurationMs,
		ThingKind.Outfit => SettingsViewModel.OutfitAnimationDurationMs,
		ThingKind.Effect => SettingsViewModel.EffectAnimationDurationMs,
		ThingKind.Missile => SettingsViewModel.MissileAnimationDurationMs,
		_ => SettingsViewModel.ItemAnimationDurationMs
	};

	private void ApplyLayoutFromThing(ThingType thing)
	{
		var group = thing.FrameGroups.FirstOrDefault();
		if (group == null) return;
		ThingWidth = (int)(group.Width == 0 ? 1u : group.Width);
		ThingHeight = (int)(group.Height == 0 ? 1u : group.Height);
		ThingLayers = (int)(group.Layers == 0 ? 1u : group.Layers);
		ThingPatternY = (int)(group.PatternY == 0 ? 1u : group.PatternY);
		ThingPatternZ = (int)(group.PatternZ == 0 ? 1u : group.PatternZ);
		if (thing.Kind == ThingKind.Outfit)
		{
			OutfitDirections = (int)(group.PatternX == 0 ? 1u : group.PatternX);
			OutfitFrames = (int)(group.Frames == 0 ? 1u : group.Frames);
		}
		else
		{
			ThingPatternX = (int)(group.PatternX == 0 ? 1u : group.PatternX);
			ThingFrames = (int)(group.Frames == 0 ? 1u : group.Frames);
		}
	}

	private static string DescribeThingSelection(SlicerThingChoiceViewModel choice, bool replacing)
	{
		var groups = choice.Thing.FrameGroups.Count;
		if (groups <= 1) return choice.DisplayName;
		return replacing
			? $"{choice.DisplayName} — {groups} frame groups; combined-sheet layout will be preserved"
			: $"{choice.DisplayName} — {groups} frame groups; combined-sheet layout will be copied";
	}

	private (bool Valid, string Message) GetLayoutStatus()
	{
		if (TryGetCombinedLayoutDimensions(out var combinedColumns, out var combinedRows))
		{
			return Columns == combinedColumns && Rows == combinedRows
				? (true, $"Combined Object Builder sheet: {combinedColumns}×{combinedRows} cells; all source frame groups will be preserved.")
				: (false, $"The selected multi-group thing needs a complete {combinedColumns}×{combinedRows} cell Object Builder sheet.");
		}

		var patternX = IsOutfit ? OutfitDirections : ThingPatternX;
		var frames = IsOutfit ? OutfitFrames : ThingFrames;
		if ((ThingWidth == 0) != (ThingHeight == 0))
			return (false, "Set both footprint values to 0 for one inferred thing, or set both to its cell dimensions.");

		long textureColumns = (long)ThingLayers * patternX * ThingPatternZ;
		long textureRows = (long)frames * ThingPatternY;
		if (textureColumns <= 0 || textureRows <= 0 || textureColumns > int.MaxValue || textureRows > int.MaxValue)
			return (false, "The frame-group dimensions are too large.");

		if (ThingWidth == 0)
		{
			if (Columns % textureColumns != 0 || Rows % textureRows != 0)
				return (false, $"Grid must be a multiple of {textureColumns} × {textureRows} cells.");
			return (true, $"One {(Columns / textureColumns)} × {(Rows / textureRows)} cell thing.");
		}

		var sheetColumns = ThingWidth * textureColumns;
		var sheetRows = ThingHeight * textureRows;
		if (sheetColumns > int.MaxValue || sheetRows > int.MaxValue || Columns % sheetColumns != 0 || Rows % sheetRows != 0)
			return (false, $"Each thing needs {sheetColumns} × {sheetRows} cells.");
		return (true, $"{(Columns / sheetColumns) * (Rows / sheetRows)} thing(s), {sheetColumns} × {sheetRows} cells each.");
	}

	private bool TryGetCombinedLayoutDimensions(out int columns, out int rows)
	{
		columns = 0;
		rows = 0;
		var source = ReplaceExisting ? SelectedReplacement?.Thing : UseTemplate ? SelectedTemplate?.Thing : null;
		if (source is not { FrameGroups.Count: > 1 }) return false;
		try
		{
			var commonWidth = source.FrameGroups.Max(group => (long)group.Width);
			var commonHeight = source.FrameGroups.Max(group => (long)group.Height);
			var totalX = source.FrameGroups.Max(group => checked((long)group.PatternZ * group.PatternX * group.Layers));
			var totalY = source.FrameGroups.Sum(group => checked((long)group.Frames * group.PatternY));
			var calculatedColumns = checked(commonWidth * totalX);
			var calculatedRows = checked(commonHeight * totalY);
			if (calculatedColumns <= 0 || calculatedRows <= 0 || calculatedColumns > int.MaxValue || calculatedRows > int.MaxValue)
				return false;
			columns = (int)calculatedColumns;
			rows = (int)calculatedRows;
			return true;
		}
		catch (OverflowException)
		{
			return false;
		}
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
		OnPropertyChanged(nameof(SplitHint));
		OnPropertyChanged(nameof(UsesCombinedLayout));
		OnPropertyChanged(nameof(OutfitFrameGroupHint));
		NotifyCommands();
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
