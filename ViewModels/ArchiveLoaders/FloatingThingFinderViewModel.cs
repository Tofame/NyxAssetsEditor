using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Input.Platform;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NyxAssets.Things;
using NyxAssetsEditor.Services.Things;
using NyxAssetsEditor.ViewModels.Core;
using NyxAssetsEditor.ViewModels.Pages;

namespace NyxAssetsEditor.ViewModels.ArchiveLoaders;

public sealed record ThingFinderOption(string Value, string DisplayName)
{
	public override string ToString() => DisplayName;
}

public sealed partial class ThingFinderFieldViewModel : ObservableObject
{
	private readonly NyxAssetsEditor.Services.Things.IThingFilterOwner _owner;
	private bool _isActive;
	private bool _booleanValue;
	private decimal? _numericValue;
	private string _value = string.Empty;
	private ThingFinderOption? _selectedOption;

	public ThingFinderFieldDescriptor Descriptor { get; }
	public string DisplayName => Descriptor.DisplayName;
	public IReadOnlyList<ThingFinderOption> Options { get; }
	public bool IsBoolean => Descriptor.ValueKind == ThingFinderValueKind.Boolean;
	public bool UsesValue => !IsBoolean;
	public bool IsChoice => Options.Count > 0;
	public bool IsNumeric => Descriptor.ValueKind == ThingFinderValueKind.Number && !IsChoice;
	public bool IsInteger => IsNumeric && Descriptor.Numeric is not { AllowsDecimal: true };
	public bool IsDecimal => IsNumeric && Descriptor.Numeric is { AllowsDecimal: true };
	public bool IsPaletteColor => Descriptor.Source == ThingFinderFieldSource.Thing
		&& IsInteger
		&& (Descriptor.Key == nameof(ThingType.LightColor) || Descriptor.Key == nameof(ThingType.MiniMapColor));
	public bool UsesStandardIntegerInput => IsInteger && !IsPaletteColor;
	public bool UsesTextValue => UsesValue && !IsNumeric && !IsChoice;
	public IReadOnlyList<FloatingThingEditorViewModel.PaletteColor> PaletteColors => FloatingThingEditorViewModel.SharedPaletteColors;
	public IBrush PaletteColorBrush => new SolidColorBrush(FloatingThingEditorViewModel.Get8BitColor(
		(int)(NumericValue ?? NumericDefault)));
	public string PalettePickerToolTip => Descriptor.Key == nameof(ThingType.LightColor)
		? "Pick Light Color"
		: "Pick Automap Color";
	public decimal NumericMinimum => Descriptor.Numeric?.Minimum ?? 0;
	public decimal NumericMaximum => Descriptor.Numeric?.Maximum ?? 65535;
	public decimal NumericIncrement => Descriptor.Numeric?.Increment ?? 1;
	public decimal NumericDefault => Descriptor.Numeric?.DefaultValue ?? 0;

	public bool IsActive
	{
		get => _isActive;
		set
		{
			if (!SetProperty(ref _isActive, value)) return;
			if (value && IsNumeric && !_numericValue.HasValue)
			{
				_numericValue = NumericDefault;
				OnPropertyChanged(nameof(NumericValue));
			}
			_owner.ScheduleFilter();
		}
	}

	public bool BooleanValue
	{
		get => _booleanValue;
		set
		{
			if (!SetProperty(ref _booleanValue, value)) return;
			_owner.ScheduleFilter();
		}
	}

	public string Value
	{
		get => _value;
		set
		{
			if (!SetProperty(ref _value, value ?? string.Empty)) return;
			_owner.ScheduleFilter();
		}
	}

	public decimal? NumericValue
	{
		get => _numericValue;
		set
		{
			if (!SetProperty(ref _numericValue, value)) return;
			OnPropertyChanged(nameof(PaletteColorBrush));
			_owner.ScheduleFilter();
		}
	}

	public ThingFinderOption? SelectedOption
	{
		get => _selectedOption;
		set
		{
			if (!SetProperty(ref _selectedOption, value)) return;
			_owner.ScheduleFilter();
		}
	}

	public ThingFinderFieldViewModel(NyxAssetsEditor.Services.Things.IThingFilterOwner owner, ThingFinderFieldDescriptor descriptor)
	{
		_owner = owner;
		Descriptor = descriptor;
		Options = GetEditorOptions(descriptor);
	}

	public ThingFinderCriterion? ToCriterion()
		=> ThingFinderFilterService.CreateCriterion(
			Descriptor,
			IsActive,
			BooleanValue,
			IsChoice ? SelectedOption?.Value : IsNumeric ? NumericValue?.ToString(CultureInfo.InvariantCulture) : Value);

	public void Clear()
	{
		_isActive = false;
		_booleanValue = false;
		_numericValue = null;
		_selectedOption = null;
		_value = string.Empty;
		OnPropertyChanged(nameof(IsActive));
		OnPropertyChanged(nameof(BooleanValue));
		OnPropertyChanged(nameof(NumericValue));
		OnPropertyChanged(nameof(PaletteColorBrush));
		OnPropertyChanged(nameof(SelectedOption));
		OnPropertyChanged(nameof(Value));
	}

	[RelayCommand]
	private void SelectPaletteColor(int colorIndex) => NumericValue = colorIndex;

	private static IReadOnlyList<ThingFinderOption> GetEditorOptions(ThingFinderFieldDescriptor descriptor)
	{
		if (descriptor.Source != ThingFinderFieldSource.Thing)
			return Array.Empty<ThingFinderOption>();

		var key = descriptor.Key;
		if (key == nameof(ThingType.MarketCategory))
			return FloatingThingEditorViewModel.MarketCategories
				.Select((name, index) => new ThingFinderOption((index + 1).ToString(CultureInfo.InvariantCulture), name))
				.ToList();
		if (key == nameof(ThingType.LensHelp))
			return FloatingThingEditorViewModel.LensHelpTypes
				.Select((name, index) => new ThingFinderOption((index + 1100).ToString(CultureInfo.InvariantCulture), name))
				.ToList();
		if (key == nameof(ThingType.DefaultAction))
			return FloatingThingEditorViewModel.DefaultActions
				.Select((name, index) => new ThingFinderOption(index.ToString(CultureInfo.InvariantCulture), name))
				.ToList();
		return Array.Empty<ThingFinderOption>();
	}
}

public sealed class ThingFinderResultViewModel : IDisposable
{
	private readonly FloatingThingFinderViewModel _owner;
	private WriteableBitmap? _previewImage;
	private bool _previewRequested;

	public ThingType Thing { get; }
	public uint DisplayedId => _owner.SourcePanel?.GetDisplayedId(Thing.Kind, Thing.Id) ?? Thing.Id;
	public string MatchDetails { get; }

	public WriteableBitmap? PreviewImage
	{
		get
		{
			if (!_previewRequested)
			{
				_previewRequested = true;
				_previewImage = _owner.SourcePanel?.GetPreviewForThing(Thing);
			}
			return _previewImage;
		}
	}

	public ThingFinderResultViewModel(FloatingThingFinderViewModel owner, ThingType thing, string matchDetails)
	{
		_owner = owner;
		Thing = thing;
		MatchDetails = matchDetails;
	}

	public IReadOnlyList<ThingFinderContextAction> GetContextActions() => _owner.GetContextActions(Thing);
	public Task ExecuteContextActionAsync(ThingFinderContextAction action) => _owner.RequestContextActionAsync(action);

	public void Dispose() => _previewImage?.Dispose();
}

public partial class FloatingThingFinderViewModel : PanelViewModelBase, IDisposable, NyxAssetsEditor.Services.Things.IThingFilterOwner
{
	private readonly AssetsViewModel _parent;
	private readonly DispatcherTimer _filterTimer = new() { Interval = TimeSpan.FromMilliseconds(175) };
	private readonly List<ThingType> _filteredThings = new();
	private readonly List<string> _extraPropertyKeys = new();
	private readonly IReadOnlyList<ThingFinderFieldDescriptor> _availableFields = ThingFinderFilterService.GetFieldDescriptors();
	private ThingKind _selectedKind;
	private int _frameGroupIndex;
	private int _currentPage = 1;
	private int _pageSize = 100;
	private bool _isGridView = true;
	private ThingFinderContextAction? _pendingContextAction;
	private bool _showConfirmation;
	private string _confirmationTitle = string.Empty;
	private string _confirmationMessage = string.Empty;
	private string _confirmationButtonText = "Confirm";

	private FloatingThingsLoaderViewModel? _sourcePanel;
	public FloatingThingsLoaderViewModel? SourcePanel => _sourcePanel;
	public string Title => "Thing Finder";

	public ObservableCollection<LooktypeArchivePairViewModel> ArchivePairs { get; } = new();

	private LooktypeArchivePairViewModel? _selectedArchivePair;
	public LooktypeArchivePairViewModel? SelectedArchivePair
	{
		get => _selectedArchivePair;
		set
		{
			var oldPanel = _sourcePanel;
			if (!SetProperty(ref _selectedArchivePair, value)) return;
			if (oldPanel != null)
			{
				oldPanel.CatalogChanged -= OnSourceCatalogChanged;
			}
			var newPanel = value?.Pair.ThingsPanel;
			_sourcePanel = newPanel;
			OnPropertyChanged(nameof(SourcePanel));
			if (newPanel != null)
			{
				newPanel.CatalogChanged += OnSourceCatalogChanged;
				_selectedKind = newPanel.SelectedSection;
				OnPropertyChanged(nameof(SelectedKind));
				OnPropertyChanged(nameof(IsItemsKind));
				OnPropertyChanged(nameof(IsOutfitsKind));
				OnPropertyChanged(nameof(IsEffectsKind));
				OnPropertyChanged(nameof(IsMissilesKind));
				RefreshExtraPropertyKeys();
				LoadFields();

				if (_targetSpritePixels != null)
				{
					var loader = newPanel.GetActiveSpriteLoader();
					if (loader != null)
					{
						_ = Task.Run(async () =>
						{
							SearchBySpriteStatus = "Scanning new sprite archive...";
							var matched = await FindMatchingSpriteIdsAsync(_targetSpritePixels, loader);
							_matchingSpriteIds = new HashSet<uint>(matched);
							SearchBySpriteStatus = $"Found {matched.Count} matching sprites in archive.";
							ScheduleFilter();
						});
					}
					else
					{
						_matchingSpriteIds = new HashSet<uint>();
						SearchBySpriteStatus = "No active sprite loader to scan.";
					}
				}
			}
			_currentPage = 1;
			ScheduleFilter();
		}
	}

	public void RefreshArchivePairs(string? preferredSpritePath = null, string? preferredThingsPath = null)
	{
		var currentSprite = preferredSpritePath ?? SelectedArchivePair?.SpritePath;
		var currentThings = preferredThingsPath ?? SelectedArchivePair?.ThingsPath;
		ArchivePairs.Clear();
		foreach (var pair in _parent.GetCompilePairs()) ArchivePairs.Add(new LooktypeArchivePairViewModel(pair));
		SelectedArchivePair = ArchivePairs.FirstOrDefault(p =>
			string.Equals(p.SpritePath, currentSprite, StringComparison.OrdinalIgnoreCase) && string.Equals(p.ThingsPath, currentThings, StringComparison.OrdinalIgnoreCase))
			?? ArchivePairs.FirstOrDefault();
	}

	[ObservableProperty]
	private WriteableBitmap? _pastedSpriteImage;

	[ObservableProperty]
	private string _searchBySpriteStatus = "No sprite loaded. Drag an image here or Paste.";

	private byte[]? _targetSpritePixels;
	private HashSet<uint>? _matchingSpriteIds;

	[ObservableProperty]
	private bool _isPropertiesExpanded = true;

	[ObservableProperty]
	private bool _isFlagsExpanded = true;

	[ObservableProperty]
	private bool _isPatternsExpanded = true;

	[ObservableProperty]
	private bool _isCustomFlagsExpanded = true;

	[ObservableProperty]
	private bool _isFindBySpriteExpanded = true;

	[RelayCommand]
	private void TogglePropertiesExpanded() => IsPropertiesExpanded = !IsPropertiesExpanded;

	[RelayCommand]
	private void ToggleFlagsExpanded() => IsFlagsExpanded = !IsFlagsExpanded;

	[RelayCommand]
	private void TogglePatternsExpanded() => IsPatternsExpanded = !IsPatternsExpanded;

	[RelayCommand]
	private void ToggleCustomFlagsExpanded() => IsCustomFlagsExpanded = !IsCustomFlagsExpanded;

	[RelayCommand]
	private void ToggleFindBySpriteExpanded() => IsFindBySpriteExpanded = !IsFindBySpriteExpanded;

	[RelayCommand]
	private void ClearSpriteFilter()
	{
		_targetSpritePixels = null;
		_matchingSpriteIds = null;
		PastedSpriteImage = null;
		SearchBySpriteStatus = "No sprite loaded. Drag an image here or Paste.";
		ScheduleFilter();
	}

	public async Task LoadSpriteFromBitmapAsync(Avalonia.Media.Imaging.Bitmap bitmap)
	{
		SearchBySpriteStatus = "Processing image...";
		try
		{
			var pixels = ProcessAndResizeBitmap(bitmap);
			if (pixels != null)
			{
				_targetSpritePixels = pixels;
				PastedSpriteImage = CreatePreviewFromPixels(pixels);
				SearchBySpriteStatus = "Scanning sprite archive...";
				
				var loader = SourcePanel?.GetActiveSpriteLoader();
				if (loader != null)
				{
					var matched = await FindMatchingSpriteIdsAsync(pixels, loader);
					_matchingSpriteIds = new HashSet<uint>(matched);
					SearchBySpriteStatus = $"Found {matched.Count} matching sprites in archive.";
				}
				else
				{
					_matchingSpriteIds = new HashSet<uint>();
					SearchBySpriteStatus = "No active sprite loader to scan.";
				}
				ScheduleFilter();
			}
			else
			{
				SearchBySpriteStatus = "Failed to decode/resize image.";
			}
		}
		catch (Exception ex)
		{
			SearchBySpriteStatus = $"Error: {ex.Message}";
		}
	}

	[RelayCommand]
	private async Task PasteSpriteFromClipboardAsync()
	{
		var clipboard = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
			? desktop.MainWindow?.Clipboard
			: null;
		if (clipboard == null) return;
		try
		{
			var bitmap = await clipboard.TryGetBitmapAsync();
			if (bitmap != null)
			{
				await LoadSpriteFromBitmapAsync(bitmap);
			}
			else
			{
				SearchBySpriteStatus = "No image found in clipboard.";
			}
		}
		catch (Exception ex)
		{
			SearchBySpriteStatus = $"Error: {ex.Message}";
		}
	}

	private byte[]? ProcessAndResizeBitmap(Avalonia.Media.Imaging.Bitmap bitmap)
	{
		try
		{
			using var ms = new System.IO.MemoryStream();
			bitmap.Save(ms, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
			ms.Position = 0;

			using var skBitmap = SkiaSharp.SKBitmap.Decode(ms);
			if (skBitmap == null) return null;

			var info = new SkiaSharp.SKImageInfo(32, 32, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul);
			using var target = new SkiaSharp.SKBitmap(info);
			using var canvas = new SkiaSharp.SKCanvas(target);
			canvas.Clear(SkiaSharp.SKColors.Transparent);
			canvas.DrawBitmap(skBitmap, new SkiaSharp.SKRect(0, 0, skBitmap.Width, skBitmap.Height), new SkiaSharp.SKRect(0, 0, 32, 32), SkiaSharp.SKSamplingOptions.Default);
			return target.Bytes;
		}
		catch
		{
			return null;
		}
	}

	private WriteableBitmap CreatePreviewFromPixels(byte[] pixels)
	{
		var dpi = new Avalonia.Vector(96, 96);
		var bmp = new WriteableBitmap(new Avalonia.PixelSize(32, 32), dpi, Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Unpremul);
		using (var buf = bmp.Lock())
		{
			System.Runtime.InteropServices.Marshal.Copy(pixels, 0, buf.Address, pixels.Length);
		}
		return bmp;
	}

	private async Task<List<uint>> FindMatchingSpriteIdsAsync(byte[] targetPixels, NyxAssetsEditor.Services.Archive.SpriteLoader loader)
	{
		return await Task.Run(() =>
		{
			var matches = new List<uint>();
			uint count = loader.SpriteCount;
			for (uint id = 1; id <= count; id++)
			{
				try
				{
					byte[] pixels = loader.LoadSpritePixels(id);
					if (PixelsMatch(pixels, targetPixels))
					{
						matches.Add(id);
					}
				}
				catch
				{
				}
			}
			return matches;
		});
	}

	private static bool PixelsMatch(byte[] a, byte[] b)
	{
		if (a.Length != b.Length) return false;
		for (int i = 0; i < a.Length; i++)
		{
			if (a[i] != b[i]) return false;
		}
		return true;
	}

	public ObservableCollection<ThingFinderFieldViewModel> PropertyFields { get; } = new();
	public ObservableCollection<ThingFinderFieldViewModel> FlagFields { get; } = new();
	public ObservableCollection<ThingFinderFieldViewModel> PatternFields { get; } = new();
	public ObservableCollection<ThingFinderFieldViewModel> ExtraPropertyFields { get; } = new();
	public ObservableCollection<ThingFinderResultViewModel> PagedResults { get; } = new();
	private readonly Dictionary<ThingKind, List<ThingFinderFieldViewModel>> _fieldsByKind = new();

	public ThingKind SelectedKind
	{
		get => _selectedKind;
		set
		{
			if (!SetProperty(ref _selectedKind, value)) return;
			OnPropertyChanged(nameof(IsItemsKind));
			OnPropertyChanged(nameof(IsOutfitsKind));
			OnPropertyChanged(nameof(IsEffectsKind));
			OnPropertyChanged(nameof(IsMissilesKind));
			RefreshExtraPropertyKeys();
			LoadFields();
			_currentPage = 1;
			ScheduleFilter();
		}
	}

	public bool IsItemsKind => SelectedKind == ThingKind.Item;
	public bool IsOutfitsKind => SelectedKind == ThingKind.Outfit;
	public bool IsEffectsKind => SelectedKind == ThingKind.Effect;
	public bool IsMissilesKind => SelectedKind == ThingKind.Missile;

	public bool HasExtraPropertyFields => ExtraPropertyFields.Count > 0;

	public int FrameGroupIndex
	{
		get => _frameGroupIndex;
		set { if (SetProperty(ref _frameGroupIndex, Math.Max(0, value))) ScheduleFilter(); }
	}

	public int CurrentPage
	{
		get => _currentPage;
		set
		{
			var clamped = Math.Clamp(value, 1, Math.Max(1, TotalPages));
			if (!SetProperty(ref _currentPage, clamped)) return;
			UpdatePage();
			NotifyPagination();
		}
	}

	public int PageSize
	{
		get => _pageSize;
		set
		{
			if (!SetProperty(ref _pageSize, Math.Max(1, value))) return;
			_currentPage = 1;
			OnPropertyChanged(nameof(CurrentPage));
			OnPropertyChanged(nameof(TotalPages));
			UpdatePage();
			NotifyPagination();
		}
	}

	public int[] AvailablePageSizes { get; } = { 25, 50, 100, 200, 500, 1000 };

	public bool IsGridView
	{
		get => _isGridView;
		set
		{
			if (!SetProperty(ref _isGridView, value)) return;
			OnPropertyChanged(nameof(IsListView));
			OnPropertyChanged(nameof(ShowGridViewContent));
			OnPropertyChanged(nameof(ShowListViewContent));
		}
	}

	public bool IsListView => !IsGridView;
	public bool ShowGridViewContent => HasResults && IsGridView;
	public bool ShowListViewContent => HasResults && IsListView;

	public int ResultCount => _filteredThings.Count;
	public IReadOnlyList<ThingType> FilteredThings => _filteredThings;
	public int TotalPages => Math.Max(1, (ResultCount + PageSize - 1) / PageSize);
	public bool HasPreviousPage => CurrentPage > 1;
	public bool HasNextPage => CurrentPage < TotalPages;
	public bool HasResults => ResultCount > 0;
	public bool HasNoResults => !HasResults;

	public bool ShowConfirmation
	{
		get => _showConfirmation;
		private set => SetProperty(ref _showConfirmation, value);
	}

	public string ConfirmationTitle
	{
		get => _confirmationTitle;
		private set => SetProperty(ref _confirmationTitle, value);
	}

	public string ConfirmationMessage
	{
		get => _confirmationMessage;
		private set => SetProperty(ref _confirmationMessage, value);
	}

	public string ConfirmationButtonText
	{
		get => _confirmationButtonText;
		private set => SetProperty(ref _confirmationButtonText, value);
	}

	public FloatingThingFinderViewModel(AssetsViewModel parent, FloatingThingsLoaderViewModel sourcePanel)
	{
		_parent = parent;
		_selectedKind = sourcePanel.SelectedSection;
		PanelWidth = 1040;
		ContentHeight = 650;
		DockState = "Floating";
		IsDefaultPosition = true;
		_filterTimer.Tick += OnFilterTimerTick;
		RefreshArchivePairs(null, sourcePanel.FilePath);
		RefreshExtraPropertyKeys();
		LoadFields();
		ApplyFilter();
	}

	public void ScheduleFilter()
	{
		_filterTimer.Stop();
		_filterTimer.Start();
	}

	[RelayCommand]
	private void SelectItemsKind() => SelectedKind = ThingKind.Item;

	[RelayCommand]
	private void SelectOutfitsKind() => SelectedKind = ThingKind.Outfit;

	[RelayCommand]
	private void SelectEffectsKind() => SelectedKind = ThingKind.Effect;

	[RelayCommand]
	private void SelectMissilesKind() => SelectedKind = ThingKind.Missile;

	[RelayCommand]
	private void ClearFilters()
	{
		foreach (var field in CurrentFields()) field.Clear();
		ScheduleFilter();
	}

	[RelayCommand]
	private void PreviousPage()
	{
		if (HasPreviousPage) CurrentPage--;
	}

	[RelayCommand]
	private void NextPage()
	{
		if (HasNextPage) CurrentPage++;
	}

	[RelayCommand]
	private void ToggleViewMode() => IsGridView = !IsGridView;

	[RelayCommand]
	private async Task ConfirmContextAction()
	{
		var action = _pendingContextAction;
		ClearConfirmation();
		if (action != null) await action.ExecuteAsync();
	}

	[RelayCommand]
	private void CancelContextAction() => ClearConfirmation();

	public IReadOnlyList<ThingFinderContextAction> GetContextActions(ThingType thing) =>
		SourcePanel == null ? Array.Empty<ThingFinderContextAction>() : _parent.GetThingFinderContextActions(SourcePanel, thing);

	public async Task RequestContextActionAsync(ThingFinderContextAction action)
	{
		if (string.IsNullOrWhiteSpace(action.ConfirmationMessage))
		{
			await action.ExecuteAsync();
			return;
		}

		_pendingContextAction = action;
		ConfirmationTitle = action.ConfirmationTitle ?? "Confirm action";
		ConfirmationMessage = action.ConfirmationMessage;
		ConfirmationButtonText = action.ConfirmationButtonText;
		ShowConfirmation = true;
	}

	private void OnFilterTimerTick(object? sender, EventArgs e)
	{
		_filterTimer.Stop();
		ApplyFilter();
	}

	private void OnSourceCatalogChanged()
	{
		RefreshExtraPropertyKeys();
		LoadFields();
		ScheduleFilter();
		OnPropertyChanged(nameof(Title));
	}

	private void RefreshExtraPropertyKeys()
	{
		if (SourcePanel == null)
		{
			_extraPropertyKeys.Clear();
			return;
		}
		var keys = SourcePanel.EnumerateThings(SelectedKind)
			.SelectMany(thing => thing.ExtraProperties.Keys)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
			.ToList();
		_extraPropertyKeys.Clear();
		_extraPropertyKeys.AddRange(keys);
	}

	private void LoadFields()
	{
		if (!_fieldsByKind.TryGetValue(SelectedKind, out var fields))
		{
			fields = CreateFields(SelectedKind);
			_fieldsByKind[SelectedKind] = fields;
		}
		else
		{
			SynchronizeExtraPropertyFields(fields);
		}

		Replace(PropertyFields, fields.Where(field => IsPropertyField(field.Descriptor)));
		Replace(FlagFields, fields.Where(field => IsFlagField(field.Descriptor)));
		Replace(PatternFields, fields.Where(field => field.Descriptor.Source == ThingFinderFieldSource.Pattern));
		Replace(ExtraPropertyFields, fields.Where(field => field.Descriptor.Source == ThingFinderFieldSource.ExtraProperty));
		OnPropertyChanged(nameof(HasExtraPropertyFields));
	}

	private List<ThingFinderFieldViewModel> CreateFields(ThingKind kind)
	{
		var fields = _availableFields
			.Where(descriptor => descriptor.Source != ThingFinderFieldSource.ExtraProperty)
			.Where(descriptor => IsAvailableForKind(descriptor.Key, kind))
			.OrderBy(GetFieldOrder)
			.ThenBy(descriptor => descriptor.DisplayName, StringComparer.OrdinalIgnoreCase)
			.Select(descriptor => new ThingFinderFieldViewModel(this, RenameForEditor(descriptor)))
			.ToList();

		fields.AddRange(_extraPropertyKeys.Select(key => new ThingFinderFieldViewModel(this,
			new ThingFinderFieldDescriptor(key, key, ThingFinderFieldSource.ExtraProperty, ThingFinderValueKind.Boolean))));
		return fields;
	}

	private void SynchronizeExtraPropertyFields(List<ThingFinderFieldViewModel> fields)
	{
		var availableKeys = _extraPropertyKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
		fields.RemoveAll(field => field.Descriptor.Source == ThingFinderFieldSource.ExtraProperty
			&& !availableKeys.Contains(field.Descriptor.Key));
		var existing = fields
			.Where(field => field.Descriptor.Source == ThingFinderFieldSource.ExtraProperty)
			.ToDictionary(field => field.Descriptor.Key, StringComparer.OrdinalIgnoreCase);
		foreach (var key in _extraPropertyKeys)
		{
			if (existing.ContainsKey(key)) continue;
			fields.Add(new ThingFinderFieldViewModel(this,
				new ThingFinderFieldDescriptor(key, key, ThingFinderFieldSource.ExtraProperty, ThingFinderValueKind.Boolean)));
		}
	}

	private IEnumerable<ThingFinderFieldViewModel> CurrentFields() =>
		PropertyFields.Concat(FlagFields).Concat(PatternFields).Concat(ExtraPropertyFields);

	private static void Replace(
		ObservableCollection<ThingFinderFieldViewModel> target,
		IEnumerable<ThingFinderFieldViewModel> values)
	{
		target.Clear();
		foreach (var value in values) target.Add(value);
	}

	private static bool IsPropertyField(ThingFinderFieldDescriptor descriptor) =>
		descriptor.Source == ThingFinderFieldSource.Thing
		&& (descriptor.ValueKind != ThingFinderValueKind.Boolean || PropertyBooleanFields.Contains(descriptor.Key));

	private static bool IsFlagField(ThingFinderFieldDescriptor descriptor) =>
		descriptor.Source == ThingFinderFieldSource.Thing
		&& descriptor.ValueKind == ThingFinderValueKind.Boolean
		&& !PropertyBooleanFields.Contains(descriptor.Key);

	private static bool IsAvailableForKind(string key, ThingKind kind)
	{
		if (kind == ThingKind.Item)
		{
			if (OutfitOnlyFields.Contains(key)) return false;
			return true;
		}

		var isSharedCommon = key is nameof(ThingType.HasOffset) or nameof(ThingType.OffsetX) or nameof(ThingType.OffsetY)
			or nameof(ThingType.HasElevation) or nameof(ThingType.Elevation)
			or nameof(ThingType.HasLight) or nameof(ThingType.LightLevel) or nameof(ThingType.LightColor);

		if (isSharedCommon) return true;

		if (kind == ThingKind.Outfit)
		{
			return key is nameof(ThingType.AnimateAlways) or nameof(ThingType.DontCenterOutfit);
		}
		if (kind == ThingKind.Effect)
		{
			return key is nameof(ThingType.AnimateAlways) or nameof(ThingType.BottomEffect);
		}
		if (kind == ThingKind.Missile)
		{
			return false;
		}

		return true;
	}

	private static ThingFinderFieldDescriptor RenameForEditor(ThingFinderFieldDescriptor descriptor)
	{
		if (descriptor.Source == ThingFinderFieldSource.Pattern)
			return descriptor with { DisplayName = descriptor.DisplayName.Replace("Pattern: ", string.Empty, StringComparison.Ordinal) };
		if (!EditorLabels.TryGetValue(descriptor.Key, out var label)) return descriptor;
		return descriptor with { DisplayName = label };
	}

	private static int GetFieldOrder(ThingFinderFieldDescriptor descriptor)
	{
		if (FieldOrder.TryGetValue(descriptor.Key, out var order)) return order;
		return descriptor.Source == ThingFinderFieldSource.Pattern ? 2000 : 1000;
	}

	internal static readonly HashSet<string> PropertyBooleanFields = new(StringComparer.Ordinal)
	{
		nameof(ThingType.IsGround), nameof(ThingType.HasLight), nameof(ThingType.MiniMap),
		nameof(ThingType.HasOffset), nameof(ThingType.HasElevation), nameof(ThingType.IsMarketItem),
		nameof(ThingType.Writable), nameof(ThingType.WritableOnce), nameof(ThingType.HasDefaultAction),
		nameof(ThingType.IsLensHelp),
	};

	internal static readonly HashSet<string> ItemOnlyFields = new(StringComparer.Ordinal)
	{
		nameof(ThingType.IsGround), nameof(ThingType.GroundSpeed), nameof(ThingType.MiniMap),
		nameof(ThingType.MiniMapColor), nameof(ThingType.Stackable), nameof(ThingType.Rotatable),
		nameof(ThingType.Writable), nameof(ThingType.WritableOnce), nameof(ThingType.MaxTextLength),
		nameof(ThingType.IsLensHelp), nameof(ThingType.LensHelp), nameof(ThingType.IsMarketItem),
		nameof(ThingType.MarketName), nameof(ThingType.MarketCategory), nameof(ThingType.MarketTradeAs),
		nameof(ThingType.MarketShowAs), nameof(ThingType.MarketRestrictProfession),
		nameof(ThingType.MarketRestrictLevel), nameof(ThingType.HasDefaultAction), nameof(ThingType.DefaultAction),
		nameof(ThingType.IsGroundBorder), nameof(ThingType.IsOnBottom), nameof(ThingType.IsOnTop),
		nameof(ThingType.IsContainer), nameof(ThingType.ForceUse), nameof(ThingType.MultiUse),
		nameof(ThingType.IsFluidContainer), nameof(ThingType.IsFluid), nameof(ThingType.IsUnpassable),
		nameof(ThingType.IsUnmoveable), nameof(ThingType.BlockPathfind), nameof(ThingType.FloorChange),
		nameof(ThingType.NoMoveAnimation), nameof(ThingType.Pickupable), nameof(ThingType.Hangable),
		nameof(ThingType.IsHorizontal), nameof(ThingType.IsVertical), nameof(ThingType.DontHide),
		nameof(ThingType.IsTranslucent), nameof(ThingType.IsLyingObject), nameof(ThingType.IsFullGround),
		nameof(ThingType.IgnoreLook), nameof(ThingType.Usable), nameof(ThingType.Wrappable),
		nameof(ThingType.Unwrappable), nameof(ThingType.BottomEffect),
	};

	internal static readonly HashSet<string> OutfitOnlyFields = new(StringComparer.Ordinal)
	{
		nameof(ThingType.DontCenterOutfit),
	};

	internal static readonly IReadOnlyDictionary<string, string> EditorLabels = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		[nameof(ThingType.IsGroundBorder)] = "Ground Border",
		[nameof(ThingType.IsOnBottom)] = "Bottom",
		[nameof(ThingType.IsOnTop)] = "Top",
		[nameof(ThingType.IsContainer)] = "Container",
		[nameof(ThingType.IsFluidContainer)] = "Fluid Container",
		[nameof(ThingType.IsFluid)] = "Fluid",
		[nameof(ThingType.IsUnpassable)] = "Unpassable",
		[nameof(ThingType.IsUnmoveable)] = "Unmoveable",
		[nameof(ThingType.IsMarketItem)] = "Market",
		[nameof(ThingType.MarketName)] = "Name",
		[nameof(ThingType.MarketCategory)] = "Category",
		[nameof(ThingType.MarketTradeAs)] = "Trade As",
		[nameof(ThingType.MarketShowAs)] = "Show As",
		[nameof(ThingType.MiniMap)] = "Automap",
		[nameof(ThingType.MiniMapColor)] = "Automap Color",
		[nameof(ThingType.LightLevel)] = "Light Intensity",
		[nameof(ThingType.MarketRestrictProfession)] = "Vocation",
		[nameof(ThingType.MarketRestrictLevel)] = "Level",
		[nameof(ThingType.MaxTextLength)] = "Max Length",
		[nameof(ThingType.HasDefaultAction)] = "Has Action",
		[nameof(ThingType.DefaultAction)] = "Action Type",
		[nameof(ThingType.IsLensHelp)] = "Lens Help",
		[nameof(ThingType.LensHelp)] = "Type",
		[nameof(ThingType.IsHorizontal)] = "Hook East",
		[nameof(ThingType.IsVertical)] = "Hook South",
		[nameof(ThingType.BlockPathfind)] = "Block Pathfinder",
		[nameof(ThingType.DontHide)] = "Don't Hide",
		[nameof(ThingType.IsTranslucent)] = "Translucent",
		[nameof(ThingType.IsLyingObject)] = "Lying Object",
		[nameof(ThingType.IsFullGround)] = "Full Ground",
		[nameof(ThingType.Usable)] = "Useable",
		[nameof(ThingType.DontCenterOutfit)] = "Don't Center Outfit",
	};

	internal static readonly IReadOnlyDictionary<string, int> FieldOrder = new Dictionary<string, int>(StringComparer.Ordinal)
	{
		[nameof(ThingType.IsGround)] = 10, [nameof(ThingType.GroundSpeed)] = 11,
		[nameof(ThingType.HasLight)] = 20, [nameof(ThingType.LightColor)] = 21, [nameof(ThingType.LightLevel)] = 22,
		[nameof(ThingType.MiniMap)] = 30, [nameof(ThingType.MiniMapColor)] = 31,
		[nameof(ThingType.HasOffset)] = 40, [nameof(ThingType.OffsetX)] = 41, [nameof(ThingType.OffsetY)] = 42,
		[nameof(ThingType.HasElevation)] = 50, [nameof(ThingType.Elevation)] = 51,
		[nameof(ThingType.IsMarketItem)] = 60, [nameof(ThingType.MarketName)] = 61,
		[nameof(ThingType.MarketCategory)] = 62, [nameof(ThingType.MarketTradeAs)] = 63,
		[nameof(ThingType.MarketShowAs)] = 64, [nameof(ThingType.MarketRestrictProfession)] = 65,
		[nameof(ThingType.MarketRestrictLevel)] = 66,
		[nameof(ThingType.Writable)] = 70, [nameof(ThingType.WritableOnce)] = 71, [nameof(ThingType.MaxTextLength)] = 72,
		[nameof(ThingType.HasDefaultAction)] = 80, [nameof(ThingType.DefaultAction)] = 81,
		[nameof(ThingType.IsLensHelp)] = 90, [nameof(ThingType.LensHelp)] = 91,
		[nameof(ThingType.IsGroundBorder)] = 100, [nameof(ThingType.IsOnBottom)] = 101, [nameof(ThingType.IsOnTop)] = 102,
		[nameof(ThingType.IsContainer)] = 110, [nameof(ThingType.Stackable)] = 111,
		[nameof(ThingType.ForceUse)] = 112, [nameof(ThingType.MultiUse)] = 113,
		[nameof(ThingType.IsFluidContainer)] = 114, [nameof(ThingType.IsFluid)] = 115,
		[nameof(ThingType.IsUnpassable)] = 116, [nameof(ThingType.IsUnmoveable)] = 117,
		[nameof(ThingType.BlockMissile)] = 118, [nameof(ThingType.BlockPathfind)] = 119,
		[nameof(ThingType.FloorChange)] = 120, [nameof(ThingType.NoMoveAnimation)] = 121,
		[nameof(ThingType.Pickupable)] = 122, [nameof(ThingType.Hangable)] = 123,
		[nameof(ThingType.IsHorizontal)] = 124, [nameof(ThingType.IsVertical)] = 125,
		[nameof(ThingType.Rotatable)] = 126, [nameof(ThingType.DontHide)] = 127,
		[nameof(ThingType.IsTranslucent)] = 128, [nameof(ThingType.IsLyingObject)] = 129,
		[nameof(ThingType.IsFullGround)] = 130, [nameof(ThingType.IgnoreLook)] = 131,
		[nameof(ThingType.Usable)] = 132, [nameof(ThingType.Wrappable)] = 133,
		[nameof(ThingType.Unwrappable)] = 134, [nameof(ThingType.BottomEffect)] = 135,
		[nameof(ThingFrameGroup.Width)] = 2000, [nameof(ThingFrameGroup.Height)] = 2001,
		[nameof(ThingFrameGroup.ExactSize)] = 2002, [nameof(ThingFrameGroup.Layers)] = 2003,
		[nameof(ThingFrameGroup.PatternX)] = 2004, [nameof(ThingFrameGroup.PatternY)] = 2005,
		[nameof(ThingFrameGroup.PatternZ)] = 2006, [nameof(ThingFrameGroup.Frames)] = 2007,
	};

	private IReadOnlyList<ThingFinderCriterion> GetCriteria()
	{
		return CurrentFields()
			.Select(field => field.ToCriterion())
			.Where(criterion => criterion != null)
			.Select(criterion => criterion!)
			.ToList();
	}

	private void ApplyFilter()
	{
		var criteria = GetCriteria();
		_filteredThings.Clear();
		if (SourcePanel != null)
		{
			var filtered = ThingFinderFilterService.Filter(
				SourcePanel.EnumerateThings(SelectedKind),
				SelectedKind,
				criteria,
				FrameGroupIndex);

			if (_targetSpritePixels != null && _matchingSpriteIds != null)
			{
				filtered = filtered.Where(thing =>
					thing.FrameGroups.Any(fg => fg.SpriteIds != null && fg.SpriteIds.Any(id => _matchingSpriteIds.Contains(id)))
				).ToList();
			}

			_filteredThings.AddRange(filtered);
		}

		_currentPage = Math.Clamp(_currentPage, 1, Math.Max(1, TotalPages));
		OnPropertyChanged(nameof(CurrentPage));
		OnPropertyChanged(nameof(ResultCount));
		OnPropertyChanged(nameof(TotalPages));
		OnPropertyChanged(nameof(HasResults));
		OnPropertyChanged(nameof(HasNoResults));
		OnPropertyChanged(nameof(ShowGridViewContent));
		OnPropertyChanged(nameof(ShowListViewContent));
		UpdatePage(criteria);
		NotifyPagination();
	}

	private void UpdatePage(IReadOnlyList<ThingFinderCriterion>? criteria = null)
	{
		criteria ??= GetCriteria();
		DisposeResults();
		var start = (CurrentPage - 1) * PageSize;
		foreach (var thing in _filteredThings.Skip(start).Take(PageSize))
		{
			var detail = criteria.Count == 0
				? "No filters"
				: string.Join("  •  ", criteria.Take(3).Select(criterion => ThingFinderFilterService.GetValueText(
					thing, criterion, FrameGroupIndex)));
			PagedResults.Add(new ThingFinderResultViewModel(this, thing, detail));
		}
	}

	private void NotifyPagination()
	{
		OnPropertyChanged(nameof(HasPreviousPage));
		OnPropertyChanged(nameof(HasNextPage));
	}

	private void ClearConfirmation()
	{
		_pendingContextAction = null;
		ShowConfirmation = false;
	}

	private void DisposeResults()
	{
		foreach (var result in PagedResults) result.Dispose();
		PagedResults.Clear();
	}

	public void Dispose()
	{
		_filterTimer.Stop();
		_filterTimer.Tick -= OnFilterTimerTick;
		if (SourcePanel != null)
		{
			SourcePanel.CatalogChanged -= OnSourceCatalogChanged;
		}
		DisposeResults();
	}
}
