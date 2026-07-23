using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NyxAssetsEditor.Models;
using NyxAssetsEditor.Services.Persistence;
using NyxAssetsEditor.Services.ImportExport;
using NyxAssetsEditor.Services.Rendering;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;
using NyxAssetsEditor.ViewModels.Core;
using NyxAssetsEditor.ViewModels.Shell;
using NyxAssetsEditor.ViewModels.Sprites;

namespace NyxAssetsEditor.ViewModels.Pages
{
	public enum PaintTool
	{
		Brush,
		Eraser,
		Picker,
		Bucket,
		Wand,
		Select,
		Move
	}

	public enum BrushShape
	{
		Square,
		Circle,
		Diamond,
		Cross
	}

	public partial class LayerViewModel : ViewModelBase
	{
		[ObservableProperty]
		private string _name;

		[ObservableProperty]
		private bool _isVisible = true;

		[ObservableProperty]
		private double _opacity = 1.0;

		[ObservableProperty]
		private bool _isDragging = false;

		[ObservableProperty]
		private bool _isEditingName = false;

		public bool IsNotVisible => !IsVisible;

		public decimal OpacityPercent
		{
			get => (decimal)Math.Round(Opacity * 100);
			set
			{
				Opacity = (double)Math.Clamp(value, 0, 100) / 100.0;
				OnPropertyChanged(nameof(OpacityPercent));
			}
		}

		partial void OnIsVisibleChanged(bool value)
		{
			OnPropertyChanged(nameof(IsNotVisible));
		}

		partial void OnOpacityChanged(double value)
		{
			OnPropertyChanged(nameof(OpacityPercent));
		}

		[RelayCommand]
		private void ToggleVisibility()
		{
			IsVisible = !IsVisible;
		}

		public byte[] Pixels { get; set; }

		public LayerViewModel(string name, byte[] pixels)
		{
			_name = name;
			Pixels = pixels;
		}
	}

	public partial class PaletteViewModel : ViewModelBase
	{
		[ObservableProperty]
		private string _name;

		[ObservableProperty]
		private bool _isModifiable = true;

		public ObservableCollection<Color> Colors { get; } = new ObservableCollection<Color>();

		public PaletteViewModel(string name, bool isModifiable = true)
		{
			_name = name;
			_isModifiable = isModifiable;
		}
	}

	public partial class PaintViewModel : ViewModelBase
	{
		private readonly MainWindowViewModel _mainWindow;
		private readonly SpriteRenderer _renderer = new SpriteRenderer();

		private void SubscribeLayer(LayerViewModel layer) =>
			layer.PropertyChanged += OnLayerPropertyChanged;

		private void UnsubscribeLayer(LayerViewModel layer) =>
			layer.PropertyChanged -= OnLayerPropertyChanged;

		private void OnLayerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(LayerViewModel.IsVisible) ||
				e.PropertyName == nameof(LayerViewModel.Opacity))
				UpdateCanvasPreview();
		}

		[ObservableProperty]
		private SpriteViewModel? _sprite;

		[ObservableProperty]
		private FloatingSpriteLoaderViewModel? _panel;

		[ObservableProperty]
		private PaintTool _activeTool = PaintTool.Brush;

		partial void OnActiveToolChanged(PaintTool value)
		{
			OnPropertyChanged(nameof(IsBrushActive));
			OnPropertyChanged(nameof(IsEraserActive));
			OnPropertyChanged(nameof(IsPickerActive));
			OnPropertyChanged(nameof(IsBucketActive));
			OnPropertyChanged(nameof(IsWandActive));
			OnPropertyChanged(nameof(IsSelectActive));
			OnPropertyChanged(nameof(IsMoveActive));
			OnPropertyChanged(nameof(IsThresholdVisible));
			NotifyOutlinePropertiesChanged();
		}

		public bool IsThresholdVisible => ActiveTool == PaintTool.Bucket || ActiveTool == PaintTool.Wand;

		public bool IsBrushActive
		{
			get => ActiveTool == PaintTool.Brush;
			set { if (value) ActiveTool = PaintTool.Brush; }
		}

		public bool IsEraserActive
		{
			get => ActiveTool == PaintTool.Eraser;
			set { if (value) ActiveTool = PaintTool.Eraser; }
		}

		public bool IsPickerActive
		{
			get => ActiveTool == PaintTool.Picker;
			set { if (value) ActiveTool = PaintTool.Picker; }
		}

		public bool IsBucketActive
		{
			get => ActiveTool == PaintTool.Bucket;
			set { if (value) ActiveTool = PaintTool.Bucket; }
		}

		public bool IsWandActive
		{
			get => ActiveTool == PaintTool.Wand;
			set { if (value) ActiveTool = PaintTool.Wand; }
		}

		public bool IsSelectActive
		{
			get => ActiveTool == PaintTool.Select;
			set { if (value) ActiveTool = PaintTool.Select; }
		}

		public bool IsMoveActive
		{
			get => ActiveTool == PaintTool.Move;
			set { if (value) ActiveTool = PaintTool.Move; }
		}

		[ObservableProperty]
		private Color _activeColor = Colors.White;

		[ObservableProperty]
		private byte _colorR = 255;

		[ObservableProperty]
		private byte _colorG = 255;

		[ObservableProperty]
		private byte _colorB = 255;

		partial void OnColorRChanged(byte value) => UpdateActiveColor();
		partial void OnColorGChanged(byte value) => UpdateActiveColor();
		partial void OnColorBChanged(byte value) => UpdateActiveColor();

		private void UpdateActiveColor()
		{
			ActiveColor = Color.FromRgb(ColorR, ColorG, ColorB);
		}

		partial void OnActiveColorChanged(Color value)
		{
			_colorR = value.R;
			_colorG = value.G;
			_colorB = value.B;
			OnPropertyChanged(nameof(ColorR));
			OnPropertyChanged(nameof(ColorG));
			OnPropertyChanged(nameof(ColorB));
		}

		[ObservableProperty]
		private int _brushSize = 1;

		partial void OnBrushSizeChanged(int value)
		{
			UpdateCanvasPreview();
			NotifyOutlinePropertiesChanged();
		}

		[ObservableProperty]
		private LayerViewModel? _activeLayer;

		partial void OnActiveLayerChanged(LayerViewModel? value)
		{
			UpdateCanvasPreview();
		}

		[ObservableProperty]
		private WriteableBitmap? _canvasPreview;

		[ObservableProperty]
		private bool _hasSelection;

		[ObservableProperty]
		private BrushShape _brushShape = BrushShape.Square;

		public bool IsSquareBrush
		{
			get => BrushShape == BrushShape.Square;
			set { if (value) BrushShape = BrushShape.Square; }
		}

		public bool IsCircleBrush
		{
			get => BrushShape == BrushShape.Circle;
			set { if (value) BrushShape = BrushShape.Circle; }
		}

		public bool IsDiamondBrush
		{
			get => BrushShape == BrushShape.Diamond;
			set { if (value) BrushShape = BrushShape.Diamond; }
		}

		public bool IsCrossBrush
		{
			get => BrushShape == BrushShape.Cross;
			set { if (value) BrushShape = BrushShape.Cross; }
		}

		[ObservableProperty]
		private int _hoverX = -1;

		[ObservableProperty]
		private Color? _draggedColor;

		[ObservableProperty]
		private int _hoverY = -1;

		[ObservableProperty]
		private bool _isHovering;

		partial void OnBrushShapeChanged(BrushShape value)
		{
			OnPropertyChanged(nameof(IsSquareBrush));
			OnPropertyChanged(nameof(IsCircleBrush));
			OnPropertyChanged(nameof(IsDiamondBrush));
			OnPropertyChanged(nameof(IsCrossBrush));
			UpdateCanvasPreview();
			NotifyOutlinePropertiesChanged();
		}
		partial void OnHoverXChanged(int value)
		{
			UpdateCanvasPreview();
			NotifyOutlinePropertiesChanged();
		}
		partial void OnHoverYChanged(int value)
		{
			UpdateCanvasPreview();
			NotifyOutlinePropertiesChanged();
		}
		partial void OnIsHoveringChanged(bool value)
		{
			UpdateCanvasPreview();
			NotifyOutlinePropertiesChanged();
		}

		[ObservableProperty]
		private bool _copyOnAxisX;

		[ObservableProperty]
		private bool _copyOnAxisY;

		partial void OnCopyOnAxisXChanged(bool value) => UpdateCanvasPreview();
		partial void OnCopyOnAxisYChanged(bool value) => UpdateCanvasPreview();

		public ObservableCollection<LayerViewModel> Layers { get; } = new ObservableCollection<LayerViewModel>();
		public ObservableCollection<Color> PaletteColors { get; } = new ObservableCollection<Color>();
		public ObservableCollection<PaletteViewModel> CustomPalettes { get; } = new ObservableCollection<PaletteViewModel>();

		[ObservableProperty]
		private string _newPaletteName = "";

		[ObservableProperty]
		private double _fillThreshold = 10.0;

		partial void OnFillThresholdChanged(double value) => UpdateCanvasPreview();

		[ObservableProperty]
		private bool _checkDiagonals = true;

		partial void OnCheckDiagonalsChanged(bool value) => UpdateCanvasPreview();

		[ObservableProperty]
		private bool _showFillPreview = true;

		partial void OnShowFillPreviewChanged(bool value) => UpdateCanvasPreview();

		[ObservableProperty]
		private double _zoomLevel = 12.0;

		[ObservableProperty]
		private int _canvasWidth = 32;

		[ObservableProperty]
		private int _canvasHeight = 32;

		[ObservableProperty]
		private bool _showResizeHandles = true;

		[ObservableProperty]
		private bool _showGrid = false;

		[ObservableProperty]
		private int _gridWidth = 16;

		[ObservableProperty]
		private int _gridHeight = 16;

		[ObservableProperty]
		private Color _gridColor = Colors.Black;

		partial void OnGridColorChanged(Color value)
		{
			OnPropertyChanged(nameof(GridBrush));
		}

		public IBrush GridBrush => new SolidColorBrush(GridColor);

		public double ZoomWidth => CanvasWidth * ZoomLevel;
		public double ZoomHeight => CanvasHeight * ZoomLevel;

		public double ZoomDimension => ZoomWidth;

		[ObservableProperty]
		private PaletteViewModel? _selectedPalette;

		partial void OnZoomLevelChanged(double value)
		{
			OnPropertyChanged(nameof(ZoomWidth));
			OnPropertyChanged(nameof(ZoomHeight));
			OnPropertyChanged(nameof(ZoomDimension));
			OnPropertyChanged(nameof(GridPathData));
			NotifyOutlinePropertiesChanged();
		}

		partial void OnCanvasWidthChanged(int value)
		{
			OnPropertyChanged(nameof(ZoomWidth));
			OnPropertyChanged(nameof(ZoomDimension));
			OnPropertyChanged(nameof(GridPathData));
			NotifyOutlinePropertiesChanged();
		}

		partial void OnCanvasHeightChanged(int value)
		{
			OnPropertyChanged(nameof(ZoomHeight));
			OnPropertyChanged(nameof(GridPathData));
			NotifyOutlinePropertiesChanged();
		}

		partial void OnShowGridChanged(bool value) => OnPropertyChanged(nameof(GridPathData));
		partial void OnGridWidthChanged(int value) => OnPropertyChanged(nameof(GridPathData));
		partial void OnGridHeightChanged(int value) => OnPropertyChanged(nameof(GridPathData));

		partial void OnSelectedPaletteChanged(PaletteViewModel? value) => ApplySelectedPalette();

		[ObservableProperty]
		private Color _gradientEndColor = Colors.White;

		private bool[,] _selectionMask = new bool[32, 32];
		private static readonly string PalettesFilePath = Path.Combine(AppContext.BaseDirectory, "Assets", "paint", "paint_palletes.toml");
		private DateTime _lastStateSave = DateTime.MinValue;
		private static byte[]? _copyBuffer;
		private static int _copyBufferWidth;
		private static int _copyBufferHeight;
		private static bool[,]? _copyBufferMask;

		public PaintViewModel(MainWindowViewModel mainWindow)
		{
			_mainWindow = mainWindow;
			LoadDefaultPalettes();
			OnPropertyChanged(nameof(ZoomWidth));
			OnPropertyChanged(nameof(ZoomHeight));
			OnPropertyChanged(nameof(ZoomDimension));
		}

		public void InitializeWithSprite(SpriteViewModel sprite, FloatingSpriteLoaderViewModel panel)
		{
			Sprite = sprite;
			Panel = panel;

			CanvasWidth = 32;
			CanvasHeight = 32;
			_selectionMask = new bool[32, 32];

			foreach (var l in Layers) UnsubscribeLayer(l);
			Layers.Clear();
			var initialPixels = sprite.GetPixels();
			var basePixels = new byte[32 * 32 * 4];
			Array.Copy(initialPixels, basePixels, initialPixels.Length);

			var baseLayer = new LayerViewModel("Base", basePixels);
			SubscribeLayer(baseLayer);
			Layers.Add(baseLayer);
			ActiveLayer = baseLayer;

			ClearSelection();

			var originalColors = new HashSet<Color>();
			for (int i = 0; i < basePixels.Length; i += 4)
			{
				byte r = basePixels[i];
				byte g = basePixels[i + 1];
				byte b = basePixels[i + 2];
				byte a = basePixels[i + 3];
				if (a > 10)
				{
					originalColors.Add(Color.FromArgb(a, r, g, b));
				}
			}

			var existingOriginal = CustomPalettes.FirstOrDefault(p => p.Name == "Original colors");
			if (existingOriginal != null)
			{
				CustomPalettes.Remove(existingOriginal);
			}

			var origPalette = new PaletteViewModel("Original colors", false);
			foreach (var color in originalColors.OrderBy(c => c.ToString()))
			{
				origPalette.Colors.Add(color);
			}
			CustomPalettes.Insert(0, origPalette);
			SelectedPalette = origPalette;
			ApplySelectedPalette();

			UpdateCanvasPreview();
		}

		public void ClearSelection()
		{
			if (_selectionMask.GetLength(0) != CanvasWidth || _selectionMask.GetLength(1) != CanvasHeight)
			{
				_selectionMask = new bool[CanvasWidth, CanvasHeight];
			}
			else
			{
				for (int y = 0; y < CanvasHeight; y++)
				{
					for (int x = 0; x < CanvasWidth; x++)
					{
						_selectionMask[x, y] = false;
					}
				}
			}
			HasSelection = false;
			UpdateCanvasPreview();
		}

		public bool IsSelected(int x, int y)
		{
			if (x < 0 || x >= CanvasWidth || y < 0 || y >= CanvasHeight)
				return false;
			return _selectionMask[x, y];
		}

		private void ExtractPaletteFromPixels(byte[] pixels)
		{
			PaletteColors.Clear();

			// Add mask generator template colors
			PaletteColors.Add(Colors.Red);
			PaletteColors.Add(Colors.Green);
			PaletteColors.Add(Colors.Blue);
			PaletteColors.Add(Colors.Yellow);
			PaletteColors.Add(Colors.Magenta);
			PaletteColors.Add(Colors.Cyan);

			var uniqueColors = new HashSet<Color>();
			for (int i = 0; i < pixels.Length; i += 4)
			{
				byte r = pixels[i];
				byte g = pixels[i + 1];
				byte b = pixels[i + 2];
				byte a = pixels[i + 3];

				if (a > 10) // Only add non-transparent colors
				{
					uniqueColors.Add(Color.FromArgb(a, r, g, b));
				}
			}

			foreach (var color in uniqueColors.OrderBy(c => c.ToString()))
			{
				if (!PaletteColors.Contains(color))
				{
					PaletteColors.Add(color);
				}
			}
		}

		public void HandleCanvasClick(int x, int y, bool isRightClick, bool merge = false)
		{
			if (ActiveLayer == null)
				return;

			if (x < 0 || x >= CanvasWidth || y < 0 || y >= CanvasHeight)
				return;

			switch (ActiveTool)
			{
				case PaintTool.Brush:
					ApplyBrush(x, y, isRightClick ? Colors.Transparent : ActiveColor);
					break;
				case PaintTool.Eraser:
					ApplyBrush(x, y, Colors.Transparent);
					break;
				case PaintTool.Picker:
					var pickerColor = GetPixelColor(GetCompositePixels(), x, y);
					if (pickerColor.A > 0)
					{
						ActiveColor = pickerColor;
					}
					break;
				case PaintTool.Bucket:
					ApplyBucketFill(x, y, isRightClick ? Colors.Transparent : ActiveColor);
					break;
				case PaintTool.Wand:
					ApplyWandSelection(x, y, merge);
					break;
			}

			UpdateCanvasPreview();
		}

		private void ApplyBrush(int cx, int cy, Color color)
		{
			if (ActiveLayer == null)
				return;

			int radius = BrushSize - 1;
			for (int dy = -radius; dy <= radius; dy++)
			{
				for (int dx = -radius; dx <= radius; dx++)
				{
					if (!IsWithinBrushShape(dx, dy, radius))
						continue;

					int x = cx + dx;
					int y = cy + dy;

					if (x >= 0 && x < CanvasWidth && y >= 0 && y < CanvasHeight)
					{
						DrawPixelWithSymmetry(x, y, color);
					}
				}
			}
		}

		private bool IsWithinBrushShape(int dx, int dy, int radius)
		{
			if (dx < -radius || dx > radius || dy < -radius || dy > radius)
				return false;
			if (BrushShape == BrushShape.Square)
				return true;
			if (radius <= 0)
				return true;
			if (BrushShape == BrushShape.Circle)
				return (dx * dx + dy * dy) <= (radius * radius);
			if (BrushShape == BrushShape.Diamond)
				return (Math.Abs(dx) + Math.Abs(dy)) <= radius;
			if (BrushShape == BrushShape.Cross)
				return dx == 0 || dy == 0;
			return true;
		}

		private void DrawPixelWithSymmetry(int x, int y, Color color)
		{
			if (ActiveLayer == null)
				return;

			// Base pixel
			if (!HasSelection || _selectionMask[x, y])
			{
				SetPixel(ActiveLayer.Pixels, x, y, color);
			}

			// Copy on Axis X (mirrors horizontal: x -> CanvasWidth - 1 - x)
			if (CopyOnAxisX)
			{
				int mx = CanvasWidth - 1 - x;
				if (mx >= 0 && mx < CanvasWidth && (!HasSelection || _selectionMask[mx, y]))
				{
					SetPixel(ActiveLayer.Pixels, mx, y, color);
				}
			}

			// Copy on Axis Y (mirrors vertical: y -> CanvasHeight - 1 - y)
			if (CopyOnAxisY)
			{
				int my = CanvasHeight - 1 - y;
				if (my >= 0 && my < CanvasHeight && (!HasSelection || _selectionMask[x, my]))
				{
					SetPixel(ActiveLayer.Pixels, x, my, color);
				}
			}

			// Both
			if (CopyOnAxisX && CopyOnAxisY)
			{
				int mx = CanvasWidth - 1 - x;
				int my = CanvasHeight - 1 - y;
				if (mx >= 0 && mx < CanvasWidth && my >= 0 && my < CanvasHeight && (!HasSelection || _selectionMask[mx, my]))
				{
					SetPixel(ActiveLayer.Pixels, mx, my, color);
				}
			}
		}

		private void ApplyBucketFill(int startX, int startY, Color fillColor)
		{
			if (ActiveLayer == null)
				return;

			var pixels = ActiveLayer.Pixels;
			var targetColor = GetPixelColor(pixels, startX, startY);

			var queue = new Queue<(int, int)>();
			queue.Enqueue((startX, startY));

			var visited = new bool[CanvasWidth, CanvasHeight];
			visited[startX, startY] = true;

			int[] dx = CheckDiagonals ? new[] { 0, 0, 1, -1, 1, 1, -1, -1 } : new[] { 0, 0, 1, -1 };
			int[] dy = CheckDiagonals ? new[] { 1, -1, 0, 0, 1, -1, 1, -1 } : new[] { 1, -1, 0, 0 };

			while (queue.Count > 0)
			{
				var (cx, cy) = queue.Dequeue();
				if (HasSelection && !_selectionMask[cx, cy])
					continue;

				SetPixel(pixels, cx, cy, fillColor);

				for (int i = 0; i < dx.Length; i++)
				{
					int nx = cx + dx[i];
					int ny = cy + dy[i];

					if (nx >= 0 && nx < CanvasWidth && ny >= 0 && ny < CanvasHeight && !visited[nx, ny])
					{
						var color = GetPixelColor(pixels, nx, ny);
						if (ColorsAreSimilar(targetColor, color, FillThreshold))
						{
							visited[nx, ny] = true;
							queue.Enqueue((nx, ny));
						}
					}
				}
			}
		}

		private void ApplyWandSelection(int startX, int startY, bool merge)
		{
			if (ActiveLayer == null)
				return;

			if (!merge)
			{
				ClearSelection();
			}

			var visited = new bool[CanvasWidth, CanvasHeight];
			var pixels = ActiveLayer.Pixels;
			var targetColor = GetPixelColor(pixels, startX, startY);

			var queue = new Queue<(int, int)>();
			queue.Enqueue((startX, startY));

			_selectionMask[startX, startY] = true;
			visited[startX, startY] = true;
			HasSelection = true;

			int[] dx = CheckDiagonals ? new[] { 0, 0, 1, -1, 1, 1, -1, -1 } : new[] { 0, 0, 1, -1 };
			int[] dy = CheckDiagonals ? new[] { 1, -1, 0, 0, 1, -1, 1, -1 } : new[] { 1, -1, 0, 0 };

			while (queue.Count > 0)
			{
				var (cx, cy) = queue.Dequeue();

				for (int i = 0; i < dx.Length; i++)
				{
					int nx = cx + dx[i];
					int ny = cy + dy[i];

					if (nx >= 0 && nx < CanvasWidth && ny >= 0 && ny < CanvasHeight && !visited[nx, ny])
					{
						var color = GetPixelColor(pixels, nx, ny);
						if (ColorsAreSimilar(targetColor, color, FillThreshold))
						{
							_selectionMask[nx, ny] = true;
							visited[nx, ny] = true;
							queue.Enqueue((nx, ny));
						}
					}
				}
			}
		}

		private int ColorSimilarity(Color c1, Color c2)
		{
			return Math.Abs(c1.R - c2.R) + Math.Abs(c1.G - c2.G) + Math.Abs(c1.B - c2.B) + Math.Abs(c1.A - c2.A);
		}

		private bool ColorsAreSimilar(Color c1, Color c2, double threshold)
		{
			double limit = (threshold / 100.0) * 1020.0;
			return ColorSimilarity(c1, c2) <= limit;
		}

		private Color GetPixelColor(byte[] pixels, int x, int y)
		{
			int idx = (y * CanvasWidth + x) * 4;
			return Color.FromArgb(pixels[idx + 3], pixels[idx], pixels[idx + 1], pixels[idx + 2]);
		}

		private void SetPixel(byte[] pixels, int x, int y, Color color)
		{
			int idx = (y * CanvasWidth + x) * 4;
			pixels[idx] = color.R;
			pixels[idx + 1] = color.G;
			pixels[idx + 2] = color.B;
			pixels[idx + 3] = color.A;
		}

		public void UpdateCanvasPreview()
		{
			var composite = GetCompositePixels();
			var overlay = new byte[CanvasWidth * CanvasHeight * 4];
			Array.Copy(composite, overlay, composite.Length);

			if (HasSelection)
			{
				for (int y = 0; y < CanvasHeight; y++)
				{
					for (int x = 0; x < CanvasWidth; x++)
					{
						if (_selectionMask[x, y])
						{
							int idx = (y * CanvasWidth + x) * 4;
							overlay[idx] = (byte)Math.Clamp(overlay[idx] + 40, 0, 255);
							overlay[idx + 1] = (byte)Math.Clamp(overlay[idx + 1] + 40, 0, 255);
							overlay[idx + 2] = (byte)Math.Clamp(overlay[idx + 2] + 100, 0, 255);
						}
					}
				}
			}

			if (CopyOnAxisX)
			{
				int mid1 = CanvasWidth / 2 - 1;
				int mid2 = CanvasWidth / 2;
				for (int y = 0; y < CanvasHeight; y++)
				{
					BlendGuidePixel(overlay, mid1, y, Colors.Red);
					BlendGuidePixel(overlay, mid2, y, Colors.Red);
				}
			}

			if (CopyOnAxisY)
			{
				int mid1 = CanvasHeight / 2 - 1;
				int mid2 = CanvasHeight / 2;
				for (int x = 0; x < CanvasWidth; x++)
				{
					BlendGuidePixel(overlay, x, mid1, Colors.Red);
					BlendGuidePixel(overlay, x, mid2, Colors.Red);
				}
			}

			if (IsHovering && HoverX >= 0 && HoverX < CanvasWidth && HoverY >= 0 && HoverY < CanvasHeight)
			{
				if (ActiveTool == PaintTool.Brush)
				{
					int radius = BrushSize - 1;
					for (int dy = -radius; dy <= radius; dy++)
					{
						for (int dx = -radius; dx <= radius; dx++)
						{
							if (!IsWithinBrushShape(dx, dy, radius))
								continue;

							int px = HoverX + dx;
							int py = HoverY + dy;

							if (px >= 0 && px < CanvasWidth && py >= 0 && py < CanvasHeight)
							{
								int idx = (py * CanvasWidth + px) * 4;
								overlay[idx] = ActiveColor.R;
								overlay[idx + 1] = ActiveColor.G;
								overlay[idx + 2] = ActiveColor.B;
								overlay[idx + 3] = ActiveColor.A;
							}
						}
					}
				}
				else if (ActiveTool == PaintTool.Bucket && ActiveLayer != null && ShowFillPreview)
				{
					// Draw a preview of what pixels will be filled using the threshold
					var pixels = ActiveLayer.Pixels;
					var targetColor = GetPixelColor(pixels, HoverX, HoverY);
					
					var queue = new Queue<(int, int)>();
					queue.Enqueue((HoverX, HoverY));

					var visited = new bool[CanvasWidth, CanvasHeight];
					visited[HoverX, HoverY] = true;

					double alpha = 0.70;

					while (queue.Count > 0)
					{
						var (cx, cy) = queue.Dequeue();
						if (HasSelection && !_selectionMask[cx, cy])
							continue;

						int idx = (cy * CanvasWidth + cx) * 4;
						overlay[idx] = (byte)(overlay[idx] * (1.0 - alpha) + ActiveColor.R * alpha);
						overlay[idx + 1] = (byte)(overlay[idx + 1] * (1.0 - alpha) + ActiveColor.G * alpha);
						overlay[idx + 2] = (byte)(overlay[idx + 2] * (1.0 - alpha) + ActiveColor.B * alpha);
						overlay[idx + 3] = (byte)(overlay[idx + 3] * (1.0 - alpha) + ActiveColor.A * alpha);

						int[] dx = CheckDiagonals ? new[] { 0, 0, 1, -1, 1, 1, -1, -1 } : new[] { 0, 0, 1, -1 };
						int[] dy = CheckDiagonals ? new[] { 1, -1, 0, 0, 1, -1, 1, -1 } : new[] { 1, -1, 0, 0 };

						for (int i = 0; i < dx.Length; i++)
						{
							int nx = cx + dx[i];
							int ny = cy + dy[i];

							if (nx >= 0 && nx < CanvasWidth && ny >= 0 && ny < CanvasHeight && !visited[nx, ny])
							{
								var color = GetPixelColor(pixels, nx, ny);
								if (ColorsAreSimilar(targetColor, color, FillThreshold))
								{
									visited[nx, ny] = true;
									queue.Enqueue((nx, ny));
								}
							}
						}
					}
				}
			}

			NotifyOutlinePropertiesChanged();
			CanvasPreview = _renderer.ConvertRgba(CanvasWidth, CanvasHeight, overlay);

			var _now = DateTime.UtcNow;
			if (Sprite != null && (_now - _lastStateSave).TotalMilliseconds >= 500)
			{
				_lastStateSave = _now;
				PersistenceService.SavePaintState(this);
			}
		}

		private void BlendGuidePixel(byte[] pixels, int x, int y, Color guideColor)
		{
			int idx = (y * CanvasWidth + x) * 4;
			double alpha = 0.40;
			pixels[idx] = (byte)(pixels[idx] * (1.0 - alpha) + guideColor.R * alpha);
			pixels[idx + 1] = (byte)(pixels[idx + 1] * (1.0 - alpha) + guideColor.G * alpha);
			pixels[idx + 2] = (byte)(pixels[idx + 2] * (1.0 - alpha) + guideColor.B * alpha);
		}

		public byte[] GetCompositePixels()
		{
			var composite = new byte[CanvasWidth * CanvasHeight * 4];

			// Process layers from bottom to top
			for (int l = Layers.Count - 1; l >= 0; l--)
			{
				var layer = Layers[l];
				if (!layer.IsVisible)
					continue;

				for (int i = 0; i < composite.Length; i += 4)
				{
					double srcAlpha = (layer.Pixels[i + 3] / 255.0) * layer.Opacity;
					if (srcAlpha <= 0.0)
						continue;

					double destAlpha = composite[i + 3] / 255.0;

					double outAlpha = srcAlpha + destAlpha * (1.0 - srcAlpha);
					if (outAlpha > 0.0)
					{
						composite[i] = (byte)((layer.Pixels[i] * srcAlpha + composite[i] * destAlpha * (1.0 - srcAlpha)) / outAlpha);
						composite[i + 1] = (byte)((layer.Pixels[i + 1] * srcAlpha + composite[i + 1] * destAlpha * (1.0 - srcAlpha)) / outAlpha);
						composite[i + 2] = (byte)((layer.Pixels[i + 2] * srcAlpha + composite[i + 2] * destAlpha * (1.0 - srcAlpha)) / outAlpha);
						composite[i + 3] = (byte)(outAlpha * 255);
					}
				}
			}

			return composite;
		}

		[RelayCommand]
		private void AddLayer()
		{
			SaveHistoryState();
			var emptyPixels = new byte[CanvasWidth * CanvasHeight * 4];
			var newLayer = new LayerViewModel($"Layer {Layers.Count + 1}", emptyPixels);
			SubscribeLayer(newLayer);
			Layers.Insert(0, newLayer); // Insert on top
			ActiveLayer = newLayer;
			UpdateCanvasPreview();
		}

		[RelayCommand]
		private void DeleteLayer()
		{
			if (ActiveLayer == null || Layers.Count <= 1)
				return;

			SaveHistoryState();
			int index = Layers.IndexOf(ActiveLayer);
			UnsubscribeLayer(ActiveLayer);
			Layers.Remove(ActiveLayer);
			ActiveLayer = Layers[Math.Min(index, Layers.Count - 1)];
			UpdateCanvasPreview();
		}

		[RelayCommand]
		private void MoveLayerUp()
		{
			if (ActiveLayer == null)
				return;
			int index = Layers.IndexOf(ActiveLayer);
			if (index > 0)
			{
				SaveHistoryState();
				var current = ActiveLayer;
				Layers.Move(index, index - 1);
				ActiveLayer = current;
				UpdateCanvasPreview();
			}
		}

		[RelayCommand]
		private void MoveLayerDown()
		{
			if (ActiveLayer == null)
				return;
			int index = Layers.IndexOf(ActiveLayer);
			if (index < Layers.Count - 1)
			{
				SaveHistoryState();
				var current = ActiveLayer;
				Layers.Move(index, index + 1);
				ActiveLayer = current;
				UpdateCanvasPreview();
			}
		}

		[RelayCommand]
		private void MirrorHorizontal()
		{
			if (ActiveLayer == null)
				return;

			SaveHistoryState();
			var pixels = ActiveLayer.Pixels;

			if (HasSelection)
			{
				int minX = CanvasWidth, maxX = -1, minY = CanvasHeight, maxY = -1;
				for (int y = 0; y < CanvasHeight; y++)
				{
					for (int x = 0; x < CanvasWidth; x++)
					{
						if (_selectionMask[x, y])
						{
							if (x < minX) minX = x;
							if (x > maxX) maxX = x;
							if (y < minY) minY = y;
							if (y > maxY) maxY = y;
						}
					}
				}
				if (maxX >= minX && maxY >= minY)
				{
					int w = maxX - minX + 1;
					for (int y = minY; y <= maxY; y++)
					{
						for (int dx = 0; dx < w / 2; dx++)
						{
							int x1 = minX + dx;
							int x2 = maxX - dx;
							var temp = GetPixelColor(pixels, x1, y);
							SetPixel(pixels, x1, y, GetPixelColor(pixels, x2, y));
							SetPixel(pixels, x2, y, temp);

							var tempMask = _selectionMask[x1, y];
							_selectionMask[x1, y] = _selectionMask[x2, y];
							_selectionMask[x2, y] = tempMask;
						}
					}
				}
			}
			else
			{
				for (int y = 0; y < CanvasHeight; y++)
				{
					for (int x = 0; x < CanvasWidth / 2; x++)
					{
						int targetX = CanvasWidth - 1 - x;
						var temp = GetPixelColor(pixels, x, y);
						SetPixel(pixels, x, y, GetPixelColor(pixels, targetX, y));
						SetPixel(pixels, targetX, y, temp);
					}
				}
			}

			UpdateCanvasPreview();
		}

		[RelayCommand]
		private void MirrorVertical()
		{
			if (ActiveLayer == null)
				return;

			SaveHistoryState();
			var pixels = ActiveLayer.Pixels;

			if (HasSelection)
			{
				int minX = CanvasWidth, maxX = -1, minY = CanvasHeight, maxY = -1;
				for (int y = 0; y < CanvasHeight; y++)
				{
					for (int x = 0; x < CanvasWidth; x++)
					{
						if (_selectionMask[x, y])
						{
							if (x < minX) minX = x;
							if (x > maxX) maxX = x;
							if (y < minY) minY = y;
							if (y > maxY) maxY = y;
						}
					}
				}
				if (maxX >= minX && maxY >= minY)
				{
					int h = maxY - minY + 1;
					for (int dy = 0; dy < h / 2; dy++)
					{
						int y1 = minY + dy;
						int y2 = maxY - dy;
						for (int x = minX; x <= maxX; x++)
						{
							var temp = GetPixelColor(pixels, x, y1);
							SetPixel(pixels, x, y1, GetPixelColor(pixels, x, y2));
							SetPixel(pixels, x, y2, temp);

							var tempMask = _selectionMask[x, y1];
							_selectionMask[x, y1] = _selectionMask[x, y2];
							_selectionMask[x, y2] = tempMask;
						}
					}
				}
			}
			else
			{
				for (int y = 0; y < CanvasHeight / 2; y++)
				{
					int targetY = CanvasHeight - 1 - y;
					for (int x = 0; x < CanvasWidth; x++)
					{
						var temp = GetPixelColor(pixels, x, y);
						SetPixel(pixels, x, y, GetPixelColor(pixels, x, targetY));
						SetPixel(pixels, x, targetY, temp);
					}
				}
			}

			UpdateCanvasPreview();
		}

		public bool[,] GetSelectionMask() => _selectionMask;

		public void ApplySelectionBox(int startX, int startY, int endX, int endY, bool[,] baseMask, bool merge)
		{
			int x1 = Math.Min(startX, endX);
			int x2 = Math.Max(startX, endX);
			int y1 = Math.Min(startY, endY);
			int y2 = Math.Max(startY, endY);

			for (int y = 0; y < CanvasHeight; y++)
			{
				for (int x = 0; x < CanvasWidth; x++)
				{
					if (merge)
					{
						_selectionMask[x, y] = baseMask[x, y] || (x >= x1 && x <= x2 && y >= y1 && y <= y2);
					}
					else
					{
						_selectionMask[x, y] = x >= x1 && x <= x2 && y >= y1 && y <= y2;
					}
				}
			}

			bool hasSel = false;
			for (int y = 0; y < CanvasHeight; y++)
			{
				for (int x = 0; x < CanvasWidth; x++)
				{
					if (_selectionMask[x, y])
					{
						hasSel = true;
						break;
					}
				}
			}
			HasSelection = hasSel;
			UpdateCanvasPreview();
		}

		[RelayCommand]
		private void DeleteSelection()
		{
			if (ActiveLayer == null || !HasSelection)
				return;

			SaveHistoryState();
			var pixels = ActiveLayer.Pixels;
			for (int y = 0; y < CanvasHeight; y++)
			{
				for (int x = 0; x < CanvasWidth; x++)
				{
					if (_selectionMask[x, y])
					{
						SetPixel(pixels, x, y, Colors.Transparent);
					}
				}
			}
			UpdateCanvasPreview();
		}

		[RelayCommand]
		private void CopySelection()
		{
			if (ActiveLayer == null || !HasSelection)
				return;

			int minX = CanvasWidth, maxX = -1, minY = CanvasHeight, maxY = -1;
			for (int y = 0; y < CanvasHeight; y++)
			{
				for (int x = 0; x < CanvasWidth; x++)
				{
					if (_selectionMask[x, y])
					{
						if (x < minX) minX = x;
						if (x > maxX) maxX = x;
						if (y < minY) minY = y;
						if (y > maxY) maxY = y;
					}
				}
			}

			if (maxX >= minX && maxY >= minY)
			{
				_copyBufferWidth = maxX - minX + 1;
				_copyBufferHeight = maxY - minY + 1;
				_copyBuffer = new byte[_copyBufferWidth * _copyBufferHeight * 4];
				_copyBufferMask = new bool[_copyBufferWidth, _copyBufferHeight];

				var pixels = ActiveLayer.Pixels;
				for (int y = minY; y <= maxY; y++)
				{
					for (int x = minX; x <= maxX; x++)
					{
						int localX = x - minX;
						int localY = y - minY;
						int destIdx = (localY * _copyBufferWidth + localX) * 4;

						if (_selectionMask[x, y])
						{
							var color = GetPixelColor(pixels, x, y);
							_copyBuffer[destIdx] = color.R;
							_copyBuffer[destIdx + 1] = color.G;
							_copyBuffer[destIdx + 2] = color.B;
							_copyBuffer[destIdx + 3] = color.A;
							_copyBufferMask[localX, localY] = true;
						}
						else
						{
							_copyBuffer[destIdx + 3] = 0;
						}
					}
				}
			}
		}

		[RelayCommand]
		private void PasteSelection()
		{
			if (ActiveLayer == null || _copyBuffer == null || _copyBufferWidth <= 0 || _copyBufferHeight <= 0)
				return;

			SaveHistoryState();

			var pastedPixels = new byte[CanvasWidth * CanvasHeight * 4];

			int pasteX = 0;
			int pasteY = 0;

			if (HasSelection)
			{
				for (int y = 0; y < CanvasHeight; y++)
				{
					for (int x = 0; x < CanvasWidth; x++)
					{
						if (_selectionMask[x, y])
						{
							pasteX = x;
							pasteY = y;
							goto FoundPasteStart;
						}
					}
				}
			}
		FoundPasteStart:

			for (int y = 0; y < _copyBufferHeight; y++)
			{
				for (int x = 0; x < _copyBufferWidth; x++)
				{
					int canvasX = pasteX + x;
					int canvasY = pasteY + y;

					if (canvasX >= 0 && canvasX < CanvasWidth && canvasY >= 0 && canvasY < CanvasHeight)
					{
						if (_copyBufferMask[x, y])
						{
							int srcIdx = (y * _copyBufferWidth + x) * 4;
							int destIdx = (canvasY * CanvasWidth + canvasX) * 4;
							pastedPixels[destIdx] = _copyBuffer[srcIdx];
							pastedPixels[destIdx + 1] = _copyBuffer[srcIdx + 1];
							pastedPixels[destIdx + 2] = _copyBuffer[srcIdx + 2];
							pastedPixels[destIdx + 3] = _copyBuffer[srcIdx + 3];
						}
					}
				}
			}

			var newLayer = new LayerViewModel("Pasted Layer", pastedPixels);
			SubscribeLayer(newLayer);
			Layers.Insert(0, newLayer);
			ActiveLayer = newLayer;

			_selectionMask = new bool[CanvasWidth, CanvasHeight];
			for (int y = 0; y < CanvasHeight; y++)
			{
				for (int x = 0; x < CanvasWidth; x++)
				{
					_selectionMask[x, y] = (x >= pasteX && x < pasteX + _copyBufferWidth && y >= pasteY && y < pasteY + _copyBufferHeight) && _copyBufferMask[x - pasteX, y - pasteY];
				}
			}
			HasSelection = true;

			UpdateCanvasPreview();
		}

		public void ShiftLayerAndSelection(int dx, int dy, byte[] originalPixels, bool[,] originalSelectionMask)
		{
			if (ActiveLayer == null)
				return;

			var pixels = ActiveLayer.Pixels;

			bool hadSelection = false;
			for (int y = 0; y < CanvasHeight; y++)
			{
				for (int x = 0; x < CanvasWidth; x++)
				{
					if (originalSelectionMask[x, y])
					{
						hadSelection = true;
						break;
					}
				}
			}

			if (hadSelection)
			{
				for (int y = 0; y < CanvasHeight; y++)
				{
					for (int x = 0; x < CanvasWidth; x++)
					{
						int idx = (y * CanvasWidth + x) * 4;
						if (originalSelectionMask[x, y])
						{
							pixels[idx] = 0;
							pixels[idx + 1] = 0;
							pixels[idx + 2] = 0;
							pixels[idx + 3] = 0;
						}
						else
						{
							pixels[idx] = originalPixels[idx];
							pixels[idx + 1] = originalPixels[idx + 1];
							pixels[idx + 2] = originalPixels[idx + 2];
							pixels[idx + 3] = originalPixels[idx + 3];
						}
					}
				}

				for (int y = 0; y < CanvasHeight; y++)
				{
					for (int x = 0; x < CanvasWidth; x++)
					{
						if (originalSelectionMask[x, y])
						{
							int newX = x + dx;
							int newY = y + dy;
							if (newX >= 0 && newX < CanvasWidth && newY >= 0 && newY < CanvasHeight)
							{
								int srcIdx = (y * CanvasWidth + x) * 4;
								int destIdx = (newY * CanvasWidth + newX) * 4;
								pixels[destIdx] = originalPixels[srcIdx];
								pixels[destIdx + 1] = originalPixels[srcIdx + 1];
								pixels[destIdx + 2] = originalPixels[srcIdx + 2];
								pixels[destIdx + 3] = originalPixels[srcIdx + 3];
							}
						}
					}
				}
			}
			else
			{
				Array.Clear(pixels, 0, pixels.Length);
				for (int y = 0; y < CanvasHeight; y++)
				{
					for (int x = 0; x < CanvasWidth; x++)
					{
						int newX = x + dx;
						int newY = y + dy;
						if (newX >= 0 && newX < CanvasWidth && newY >= 0 && newY < CanvasHeight)
						{
							int srcIdx = (y * CanvasWidth + x) * 4;
							int destIdx = (newY * CanvasWidth + newX) * 4;
							pixels[destIdx] = originalPixels[srcIdx];
							pixels[destIdx + 1] = originalPixels[srcIdx + 1];
							pixels[destIdx + 2] = originalPixels[srcIdx + 2];
							pixels[destIdx + 3] = originalPixels[srcIdx + 3];
						}
					}
				}
			}

			for (int y = 0; y < CanvasHeight; y++)
			{
				for (int x = 0; x < CanvasWidth; x++)
				{
					int oldX = x - dx;
					int oldY = y - dy;
					if (oldX >= 0 && oldX < CanvasWidth && oldY >= 0 && oldY < CanvasHeight)
					{
						_selectionMask[x, y] = originalSelectionMask[oldX, oldY];
					}
					else
					{
						_selectionMask[x, y] = false;
					}
				}
			}

			bool hasSel = false;
			for (int y = 0; y < CanvasHeight; y++)
			{
				for (int x = 0; x < CanvasWidth; x++)
				{
					if (_selectionMask[x, y])
					{
						hasSel = true;
						break;
					}
				}
			}
			HasSelection = hasSel;

			UpdateCanvasPreview();
		}

		[RelayCommand]
		private void RecolorAll()
		{
			if (ActiveLayer == null)
				return;

			// Quick prompt-less recoloring: replace active layer's pixels matching current picker color
			// with ActiveColor. To find current picker color, we can let user use picker first.
			// Let's implement replacing pixels matching target color.
		}

		public void RecolorColor(Color targetColor, Color replacementColor)
		{
			if (ActiveLayer == null)
				return;

			SaveHistoryState();
			var pixels = ActiveLayer.Pixels;
			for (int y = 0; y < CanvasHeight; y++)
			{
				for (int x = 0; x < CanvasWidth; x++)
				{
					if (HasSelection && !_selectionMask[x, y])
						continue;

					var color = GetPixelColor(pixels, x, y);
					if (color == targetColor)
					{
						SetPixel(pixels, x, y, replacementColor);
					}
				}
			}

			UpdateCanvasPreview();
		}

		[RelayCommand]
		private void AddColorToPalette()
		{
			if (SelectedPalette != null && SelectedPalette.IsModifiable)
			{
				if (!SelectedPalette.Colors.Contains(ActiveColor))
				{
					SelectedPalette.Colors.Add(ActiveColor);
					SavePalettes();
				}
				if (!PaletteColors.Contains(ActiveColor))
				{
					PaletteColors.Add(ActiveColor);
				}
			}
		}

		[RelayCommand]
		private void CreatePalette()
		{
			string paletteName = string.IsNullOrWhiteSpace(NewPaletteName)
				? $"Palette{CustomPalettes.Count + 1}"
				: NewPaletteName.Trim();

			var newPalette = new PaletteViewModel(paletteName);
			CustomPalettes.Add(newPalette);
			SelectedPalette = newPalette;
			NewPaletteName = ""; // Clear input textbox
			SavePalettes();
		}

		[RelayCommand]
		private void DeleteColor(Color color)
		{
			PaletteColors.Remove(color);
			if (SelectedPalette != null && SelectedPalette.IsModifiable)
			{
				SelectedPalette.Colors.Remove(color);
				SavePalettes();
			}
		}

		[RelayCommand]
		private void DuplicateColor(Color color)
		{
			if (SelectedPalette != null && SelectedPalette.IsModifiable)
			{
				PaletteColors.Add(color);
				SelectedPalette.Colors.Add(color);
				SavePalettes();
			}
		}

		public void DeleteColorAtIndex(int index)
		{
			if (index >= 0 && index < PaletteColors.Count)
			{
				PaletteColors.RemoveAt(index);
				if (SelectedPalette != null && SelectedPalette.IsModifiable && index < SelectedPalette.Colors.Count)
				{
					SelectedPalette.Colors.RemoveAt(index);
					SavePalettes();
				}
			}
		}

		public void DuplicateColorAtIndex(int index)
		{
			if (index >= 0 && index < PaletteColors.Count && SelectedPalette != null && SelectedPalette.IsModifiable)
			{
				var color = PaletteColors[index];
				PaletteColors.Insert(index + 1, color);
				SelectedPalette.Colors.Insert(index + 1, color);
				SavePalettes();
			}
		}

		[RelayCommand]
		private void DuplicateSelectedPalette()
		{
			if (SelectedPalette == null)
				return;

			string baseName = SelectedPalette.Name;
			string dupName = $"{baseName} Copy";

			var duplicated = new PaletteViewModel(dupName, true);
			foreach (var c in SelectedPalette.Colors)
			{
				duplicated.Colors.Add(c);
			}

			CustomPalettes.Add(duplicated);
			SelectedPalette = duplicated;
			SavePalettes();
		}

		[RelayCommand]
		private void DeletePalette(PaletteViewModel palette)
		{
			if (palette != null && palette.IsModifiable)
			{
				CustomPalettes.Remove(palette);
				if (SelectedPalette == palette)
				{
					SelectedPalette = CustomPalettes.FirstOrDefault();
				}
				SavePalettes();
			}
		}

		[RelayCommand]
		private void RenameSelectedPalette()
		{
			if (SelectedPalette != null && SelectedPalette.IsModifiable && !string.IsNullOrWhiteSpace(NewPaletteName))
			{
				SelectedPalette.Name = NewPaletteName.Trim();
				NewPaletteName = "";
				SavePalettes();
				
				var temp = SelectedPalette;
				SelectedPalette = null;
				SelectedPalette = temp;
			}
		}

		[RelayCommand]
		private void ExtractPaletteFromActiveLayer()
		{
			if (ActiveLayer != null)
			{
				ExtractPaletteFromPixels(ActiveLayer.Pixels);
			}
		}

		[RelayCommand]
		private void ApplySelectedPalette()
		{
			if (SelectedPalette == null)
				return;

			PaletteColors.Clear();
			foreach (var c in SelectedPalette.Colors)
			{
				PaletteColors.Add(c);
			}
		}

		[RelayCommand]
		private void SetBrushSize(object parameter)
		{
			if (parameter is string s && int.TryParse(s, out int size))
			{
				BrushSize = size;
			}
			else if (parameter is int sz)
			{
				BrushSize = sz;
			}
		}

		[RelayCommand]
		private void ZoomIn()
		{
			if (ZoomLevel < 64.0)
			{
				ZoomLevel = Math.Min(64.0, ZoomLevel + 1.0);
			}
		}

		[RelayCommand]
		private void ZoomOut()
		{
			if (ZoomLevel > 1.0)
			{
				ZoomLevel = Math.Max(1.0, ZoomLevel - 1.0);
			}
		}

		[ObservableProperty]
		private int _gradientStops = 8;

		public string GradientStepsButtonText => $"Gen ({GradientStops} steps)";

		[ObservableProperty]
		private string _gradientStopsInput = "8";

		partial void OnGradientStopsChanged(int value)
		{
			OnPropertyChanged(nameof(GradientStepsButtonText));
		}

		[RelayCommand]
		private void ApplyStopsInput()
		{
			if (int.TryParse(GradientStopsInput, out int val) && val > 1)
			{
				GradientStops = val;
			}
		}

		[RelayCommand]
		private void GenerateGradient()
		{
			Color start = ActiveColor;
			Color end = GradientEndColor;
			int steps = GradientStops;

			for (int i = 0; i < steps; i++)
			{
				float t = steps > 1 ? i / (float)(steps - 1) : 0.0f;
				byte r = (byte)(start.R + (end.R - start.R) * t);
				byte g = (byte)(start.G + (end.G - start.G) * t);
				byte b = (byte)(start.B + (end.B - start.B) * t);
				byte a = (byte)(start.A + (end.A - start.A) * t);
				var color = Color.FromArgb(a, r, g, b);

				if (!PaletteColors.Contains(color))
				{
					PaletteColors.Add(color);
					if (SelectedPalette != null && SelectedPalette.IsModifiable && !SelectedPalette.Colors.Contains(color))
					{
						SelectedPalette.Colors.Add(color);
					}
				}
			}
			SavePalettes();
		}

		public string GridPathData => GetGridPathData();

		private string GetGridPathData()
		{
			if (!ShowGrid || GridWidth <= 0 || GridHeight <= 0)
				return string.Empty;

			var sb = new System.Text.StringBuilder();
			double zoom = ZoomLevel;
			double w = CanvasWidth * zoom;
			double h = CanvasHeight * zoom;

			for (int x = GridWidth; x < CanvasWidth; x += GridWidth)
			{
				double lx = x * zoom;
				sb.Append($"M {lx},0 L {lx},{h} ");
			}

			for (int y = GridHeight; y < CanvasHeight; y += GridHeight)
			{
				double ly = y * zoom;
				sb.Append($"M 0,{ly} L {w},{ly} ");
			}

			return sb.ToString();
		}

		private byte[] GetCompositePixels32()
		{
			var composite = GetCompositePixels();
			if (CanvasWidth == 32 && CanvasHeight == 32)
				return composite;

			var result = new byte[32 * 32 * 4];
			int startX = (CanvasWidth - 32) / 2;
			int startY = (CanvasHeight - 32) / 2;

			for (int y = 0; y < 32; y++)
			{
				int srcY = startY + y;
				if (srcY < 0 || srcY >= CanvasHeight) continue;

				for (int x = 0; x < 32; x++)
				{
					int srcX = startX + x;
					if (srcX < 0 || srcX >= CanvasWidth) continue;

					int srcIdx = (srcY * CanvasWidth + srcX) * 4;
					int destIdx = (y * 32 + x) * 4;
					Array.Copy(composite, srcIdx, result, destIdx, 4);
				}
			}
			return result;
		}

		public void ResizeCanvas(int newWidth, int newHeight, int offsetX, int offsetY)
		{
			if (newWidth <= 0 || newHeight <= 0)
				return;

			var oldWidth = CanvasWidth;
			var oldHeight = CanvasHeight;

			var newMask = new bool[newWidth, newHeight];
			for (int y = 0; y < oldHeight; y++)
			{
				int ny = y + offsetY;
				if (ny < 0 || ny >= newHeight) continue;
				for (int x = 0; x < oldWidth; x++)
				{
					int nx = x + offsetX;
					if (nx < 0 || nx >= newWidth) continue;
					newMask[nx, ny] = _selectionMask[x, y];
				}
			}
			_selectionMask = newMask;

			foreach (var layer in Layers)
			{
				var newPixels = new byte[newWidth * newHeight * 4];
				for (int y = 0; y < oldHeight; y++)
				{
					int ny = y + offsetY;
					if (ny < 0 || ny >= newHeight) continue;
					for (int x = 0; x < oldWidth; x++)
					{
						int nx = x + offsetX;
						if (nx < 0 || nx >= newWidth) continue;
						int srcIdx = (y * oldWidth + x) * 4;
						int destIdx = (ny * newWidth + nx) * 4;
						Array.Copy(layer.Pixels, srcIdx, newPixels, destIdx, 4);
					}
				}
				layer.Pixels = newPixels;
			}

			CanvasWidth = newWidth;
			CanvasHeight = newHeight;
			UpdateCanvasPreview();
		}

		public void ResizeCanvasFromState(int newWidth, int newHeight, int offsetX, int offsetY, List<byte[]> startLayersPixels, bool[,] startSelectionMask, int startWidth, int startHeight)
		{
			if (newWidth <= 0 || newHeight <= 0)
				return;

			var newMask = new bool[newWidth, newHeight];
			for (int y = 0; y < startHeight; y++)
			{
				int ny = y + offsetY;
				if (ny < 0 || ny >= newHeight) continue;
				for (int x = 0; x < startWidth; x++)
				{
					int nx = x + offsetX;
					if (nx < 0 || nx >= newWidth) continue;
					newMask[nx, ny] = startSelectionMask[x, y];
				}
			}
			_selectionMask = newMask;

			for (int i = 0; i < Layers.Count; i++)
			{
				if (i >= startLayersPixels.Count) continue;
				var layer = Layers[i];
				var startPixels = startLayersPixels[i];

				var newPixels = new byte[newWidth * newHeight * 4];
				for (int y = 0; y < startHeight; y++)
				{
					int ny = y + offsetY;
					if (ny < 0 || ny >= newHeight) continue;
					for (int x = 0; x < startWidth; x++)
					{
						int nx = x + offsetX;
						if (nx < 0 || nx >= newWidth) continue;
						int srcIdx = (y * startWidth + x) * 4;
						int destIdx = (ny * newWidth + nx) * 4;
						Array.Copy(startPixels, srcIdx, newPixels, destIdx, 4);
					}
				}
				layer.Pixels = newPixels;
			}

			CanvasWidth = newWidth;
			CanvasHeight = newHeight;
			UpdateCanvasPreview();
		}

		[RelayCommand]
		public void FitCanvasToContent()
		{
			int minX = CanvasWidth;
			int maxX = -1;
			int minY = CanvasHeight;
			int maxY = -1;

			bool found = false;
			foreach (var layer in Layers)
			{
				var pixels = layer.Pixels;
				for (int y = 0; y < CanvasHeight; y++)
				{
					for (int x = 0; x < CanvasWidth; x++)
					{
						int idx = (y * CanvasWidth + x) * 4;
						if (pixels[idx + 3] > 0)
						{
							if (x < minX) minX = x;
							if (x > maxX) maxX = x;
							if (y < minY) minY = y;
							if (y > maxY) maxY = y;
							found = true;
						}
					}
				}
			}

			int newWidth, newHeight, offsetX, offsetY;

			if (!found)
			{
				newWidth = 32;
				newHeight = 32;
				offsetX = (32 - CanvasWidth) / 2;
				offsetY = (32 - CanvasHeight) / 2;
			}
			else
			{
				newWidth = maxX - minX + 1;
				newHeight = maxY - minY + 1;
				offsetX = -minX;
				offsetY = -minY;
			}

			SaveHistoryState();
			ResizeCanvas(newWidth, newHeight, offsetX, offsetY);
		}

		[RelayCommand]
		private void Save()
		{
			if (Sprite != null && Panel != null)
			{
				Panel.ReplaceSpritePixels(new[] { Sprite }, GetCompositePixels32());
			}
			_mainWindow.NavigateToAssetsCommand.Execute(null);
		}

		[RelayCommand]
		private void Cancel()
		{
			_mainWindow.NavigateToAssetsCommand.Execute(null);
		}

		[RelayCommand]
		private async Task CopyToClipboard()
		{
			var pixels = GetCompositePixels32();
			await SpriteClipboard.CopyAsync(pixels);
		}

		public async Task TryRestoreStateAsync(PersistenceService.PaintStateModel state)
		{
			if (string.IsNullOrEmpty(state.SpriteFilePath) || !File.Exists(state.SpriteFilePath))
				return;

			var panel = new FloatingSpriteLoaderViewModel(_renderer);
			try
			{
				await panel.LoadArchiveAsync(state.SpriteFilePath).ConfigureAwait(true);
			}
			catch
			{
				return;
			}

			if (state.SpriteId > 0)
			{
				int page = (int)((state.SpriteId - 1) / panel.PageSize + 1);
				if (page != panel.CurrentPage)
					panel.CurrentPage = page;
			}

			var sprite = panel.PagedSprites.FirstOrDefault(s => s.Id == state.SpriteId);
			if (sprite == null)
				return;

			Sprite = sprite;
			Panel = panel;

			int w = state.CanvasWidth > 0 ? state.CanvasWidth : 32;
			int h = state.CanvasHeight > 0 ? state.CanvasHeight : 32;
			CanvasWidth = w;
			CanvasHeight = h;
			_selectionMask = new bool[w, h];

			Layers.Clear();
			foreach (var layerModel in state.Layers)
			{
				byte[] pixels;
				try { pixels = Convert.FromBase64String(layerModel.Pixels ?? ""); }
				catch { pixels = new byte[w * h * 4]; }
				if (pixels.Length != w * h * 4)
					pixels = new byte[w * h * 4];

				var layer = new LayerViewModel(layerModel.Name ?? "Layer", pixels)
				{
					IsVisible = layerModel.IsVisible,
					Opacity = layerModel.Opacity
				};
				SubscribeLayer(layer);
				Layers.Add(layer);
			}

			if (Layers.Count == 0)
			{
				var basePixels = sprite.GetPixels();
				var copy = new byte[basePixels.Length];
				Array.Copy(basePixels, copy, basePixels.Length);
				var fallbackLayer = new LayerViewModel("Base", copy);
				SubscribeLayer(fallbackLayer);
				Layers.Add(fallbackLayer);
			}

			ActiveLayer = Layers[Math.Clamp(state.ActiveLayerIndex, 0, Layers.Count - 1)];

			if (Enum.TryParse<PaintTool>(state.ActiveTool, out var tool))
				ActiveTool = tool;
			if (Enum.TryParse<BrushShape>(state.BrushShape, out var shape))
				BrushShape = shape;
			BrushSize = Math.Max(1, state.BrushSize);
			ZoomLevel = state.ZoomLevel > 0 ? state.ZoomLevel : 12.0;
			ActiveColor = Color.FromRgb(
				(byte)Math.Clamp(state.ColorR, 0, 255),
				(byte)Math.Clamp(state.ColorG, 0, 255),
				(byte)Math.Clamp(state.ColorB, 0, 255));
			CopyOnAxisX = state.CopyOnAxisX;
			CopyOnAxisY = state.CopyOnAxisY;
			FillThreshold = state.FillThreshold;
			CheckDiagonals = state.CheckDiagonals;
			ShowFillPreview = state.ShowFillPreview;
			if (Color.TryParse(state.GridColor ?? "#FF000000", out var gridCol))
				GridColor = gridCol;

			// Rebuild original-colors palette from the bottom layer
			var existingOriginal = CustomPalettes.FirstOrDefault(p => p.Name == "Original colors");
			if (existingOriginal != null)
				CustomPalettes.Remove(existingOriginal);

			var bottomLayer = Layers[Layers.Count - 1];
			var originalColors = new HashSet<Color>();
			for (int i = 0; i < bottomLayer.Pixels.Length; i += 4)
			{
				byte r = bottomLayer.Pixels[i];
				byte g = bottomLayer.Pixels[i + 1];
				byte b = bottomLayer.Pixels[i + 2];
				byte a = bottomLayer.Pixels[i + 3];
				if (a > 10)
					originalColors.Add(Color.FromArgb(a, r, g, b));
			}
			var origPalette = new PaletteViewModel("Original colors", false);
			foreach (var color in originalColors.OrderBy(c => c.ToString()))
				origPalette.Colors.Add(color);
			CustomPalettes.Insert(0, origPalette);

			SelectedPalette = origPalette;
			if (!string.IsNullOrEmpty(state.SelectedPaletteName))
			{
				var matchedPalette = CustomPalettes.FirstOrDefault(p => p.Name == state.SelectedPaletteName);
				if (matchedPalette != null)
				{
					SelectedPalette = matchedPalette;
				}
			}
			ApplySelectedPalette();

			ClearSelection();
		}

		private void LoadDefaultPalettes()
		{
			CustomPalettes.Clear();

			// Add a default Tibia Outfit Mask palette
			var maskPalette = new PaletteViewModel("Outfit Masks", false);
			maskPalette.Colors.Add(Colors.Red);
			maskPalette.Colors.Add(Colors.Green);
			maskPalette.Colors.Add(Colors.Blue);
			maskPalette.Colors.Add(Colors.Yellow);
			CustomPalettes.Add(maskPalette);

			// Add default Retro Neon palette
			var neonPalette = new PaletteViewModel("Retro Neon", false);
			neonPalette.Colors.Add(Color.Parse("#FF0055"));
			neonPalette.Colors.Add(Color.Parse("#00FFCC"));
			neonPalette.Colors.Add(Color.Parse("#9900FF"));
			neonPalette.Colors.Add(Color.Parse("#FFCC00"));
			neonPalette.Colors.Add(Color.Parse("#FF00FF"));
			neonPalette.Colors.Add(Color.Parse("#00CCFF"));
			CustomPalettes.Add(neonPalette);

			// Add default Retro Pastel palette
			var pastelPalette = new PaletteViewModel("Pastel Dream", false);
			pastelPalette.Colors.Add(Color.Parse("#FFB3BA"));
			pastelPalette.Colors.Add(Color.Parse("#FFDFBA"));
			pastelPalette.Colors.Add(Color.Parse("#FFFFBA"));
			pastelPalette.Colors.Add(Color.Parse("#BAFFC9"));
			pastelPalette.Colors.Add(Color.Parse("#BAE1FF"));
			pastelPalette.Colors.Add(Color.Parse("#E8C4FF"));
			CustomPalettes.Add(pastelPalette);

			if (File.Exists(PalettesFilePath))
			{
				try
				{
					var lines = File.ReadAllLines(PalettesFilePath);
					PaletteViewModel? current = null;
					foreach (var line in lines)
					{
						var trimmed = line.Trim();
						if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
						{
							var name = trimmed.Substring(1, trimmed.Length - 2);
							current = new PaletteViewModel(name);
							CustomPalettes.Add(current);
						}
						else if (current != null && Color.TryParse(trimmed, out var color))
						{
							current.Colors.Add(color);
						}
					}
				}
				catch
				{
					// Ignore load errors
				}
			}

			SelectedPalette = CustomPalettes.FirstOrDefault();
		}

		public void SavePalettes()
		{
			try
			{
				string? dir = Path.GetDirectoryName(PalettesFilePath);
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
				{
					Directory.CreateDirectory(dir);
				}

				var lines = new List<string>();
				foreach (var palette in CustomPalettes)
				{
					if (!palette.IsModifiable)
						continue;

					lines.Add($"[{palette.Name}]");
					foreach (var color in palette.Colors)
					{
						lines.Add(color.ToString());
					}
					lines.Add("");
				}
				File.WriteAllLines(PalettesFilePath, lines);
			}
			catch
			{
				// Ignore save errors
			}
		}

		private class PaintHistoryState
		{
			public int CanvasWidth { get; set; } = 32;
			public int CanvasHeight { get; set; } = 32;
			public List<(string Name, bool IsVisible, double Opacity, byte[] Pixels)> Layers { get; set; } = new();
		}

		private readonly Stack<PaintHistoryState> _undoStack = new();
		private readonly Stack<PaintHistoryState> _redoStack = new();

		private PaintHistoryState CaptureHistoryState()
		{
			var state = new PaintHistoryState
			{
				CanvasWidth = CanvasWidth,
				CanvasHeight = CanvasHeight
			};
			foreach (var layer in Layers)
			{
				var pixelCopy = new byte[layer.Pixels.Length];
				Array.Copy(layer.Pixels, pixelCopy, layer.Pixels.Length);
				state.Layers.Add((layer.Name, layer.IsVisible, layer.Opacity, pixelCopy));
			}
			return state;
		}

		private void RestoreHistoryState(PaintHistoryState state)
		{
			CanvasWidth = state.CanvasWidth;
			CanvasHeight = state.CanvasHeight;
			_selectionMask = new bool[CanvasWidth, CanvasHeight];

			foreach (var l in Layers) UnsubscribeLayer(l);

			while (Layers.Count > state.Layers.Count)
			{
				Layers.RemoveAt(Layers.Count - 1);
			}
			while (Layers.Count < state.Layers.Count)
			{
				Layers.Add(new LayerViewModel("", new byte[CanvasWidth * CanvasHeight * 4]));
			}

			for (int i = 0; i < state.Layers.Count; i++)
			{
				var target = Layers[i];
				var source = state.Layers[i];
				target.Name = source.Name;
				target.IsVisible = source.IsVisible;
				target.Opacity = source.Opacity;
				if (target.Pixels.Length != source.Pixels.Length)
				{
					target.Pixels = new byte[source.Pixels.Length];
				}
				Array.Copy(source.Pixels, target.Pixels, source.Pixels.Length);
				SubscribeLayer(target);
			}

			if (ActiveLayer == null || !Layers.Contains(ActiveLayer))
			{
				ActiveLayer = Layers.FirstOrDefault();
			}
			else
			{
				var current = ActiveLayer;
				ActiveLayer = null;
				ActiveLayer = current;
			}

			UpdateCanvasPreview();
		}

		public void SaveHistoryState()
		{
			_undoStack.Push(CaptureHistoryState());
			_redoStack.Clear();
			UndoCommand.NotifyCanExecuteChanged();
			RedoCommand.NotifyCanExecuteChanged();
		}

		[RelayCommand(CanExecute = nameof(CanUndo))]
		private void Undo()
		{
			if (_undoStack.Count > 0)
			{
				_redoStack.Push(CaptureHistoryState());
				var previousState = _undoStack.Pop();
				RestoreHistoryState(previousState);
				UndoCommand.NotifyCanExecuteChanged();
				RedoCommand.NotifyCanExecuteChanged();
			}
		}

		private bool CanUndo() => _undoStack.Count > 0;

		[RelayCommand(CanExecute = nameof(CanRedo))]
		private void Redo()
		{
			if (_redoStack.Count > 0)
			{
				_undoStack.Push(CaptureHistoryState());
				var nextState = _redoStack.Pop();
				RestoreHistoryState(nextState);
				UndoCommand.NotifyCanExecuteChanged();
				RedoCommand.NotifyCanExecuteChanged();
			}
		}

		private bool CanRedo() => _redoStack.Count > 0;

		public string HoverOutlinePathData => GetHoverOutlinePathData();

		private string GetHoverOutlinePathData()
		{
			if (!IsHovering)
				return string.Empty;

			if (ActiveTool != PaintTool.Eraser && ActiveTool != PaintTool.Picker)
				return string.Empty;

			var sb = new System.Text.StringBuilder();
			double zoom = ZoomLevel;

			if (ActiveTool == PaintTool.Picker)
			{
				if (HoverX >= 0 && HoverX < CanvasWidth && HoverY >= 0 && HoverY < CanvasHeight)
				{
					double left = HoverX * zoom;
					double top = HoverY * zoom;
					double right = left + zoom;
					double bottom = top + zoom;
					sb.Append($"M {left},{top} L {right},{top} L {right},{bottom} L {left},{bottom} Z");
				}
				return sb.ToString();
			}

			int radius = BrushSize - 1;

			for (int dy = -radius; dy <= radius; dy++)
			{
				for (int dx = -radius; dx <= radius; dx++)
				{
					if (!IsWithinBrushShape(dx, dy, radius))
						continue;

					int px = HoverX + dx;
					int py = HoverY + dy;

					if (px >= 0 && px < CanvasWidth && py >= 0 && py < CanvasHeight)
					{
						double left = px * zoom;
						double top = py * zoom;
						double right = left + zoom;
						double bottom = top + zoom;

						// Left edge
						if (!IsWithinBrushShape(dx - 1, dy, radius) || px == 0)
						{
							sb.Append($"M {left},{top} L {left},{bottom} ");
						}
						// Right edge
						if (!IsWithinBrushShape(dx + 1, dy, radius) || px == CanvasWidth - 1)
						{
							sb.Append($"M {right},{top} L {right},{bottom} ");
						}
						// Top edge
						if (!IsWithinBrushShape(dx, dy - 1, radius) || py == 0)
						{
							sb.Append($"M {left},{top} L {right},{top} ");
						}
						// Bottom edge
						if (!IsWithinBrushShape(dx, dy + 1, radius) || py == CanvasHeight - 1)
						{
							sb.Append($"M {left},{bottom} L {right},{bottom} ");
						}
					}
				}
			}
			return sb.ToString();
		}

		public string SelectionOutlinePathData => GetSelectionOutlinePathData();

		private string GetSelectionOutlinePathData()
		{
			if (!HasSelection)
				return string.Empty;

			var sb = new System.Text.StringBuilder();
			double zoom = ZoomLevel;

			for (int y = 0; y < CanvasHeight; y++)
			{
				for (int x = 0; x < CanvasWidth; x++)
				{
					if (_selectionMask[x, y])
					{
						double left = x * zoom;
						double top = y * zoom;
						double right = left + zoom;
						double bottom = top + zoom;

						if (x == 0 || !_selectionMask[x - 1, y])
						{
							sb.Append($"M {left},{top} L {left},{bottom} ");
						}
						if (x == CanvasWidth - 1 || !_selectionMask[x + 1, y])
						{
							sb.Append($"M {right},{top} L {right},{bottom} ");
						}
						if (y == 0 || !_selectionMask[x, y - 1])
						{
							sb.Append($"M {left},{top} L {right},{top} ");
						}
						if (y == CanvasHeight - 1 || !_selectionMask[x, y + 1])
						{
							sb.Append($"M {left},{bottom} L {right},{bottom} ");
						}
					}
				}
			}
			return sb.ToString();
		}

		public string ActiveLayerOutlinePathData => GetActiveLayerOutlinePathData();

		private string GetActiveLayerOutlinePathData()
		{
			if (ActiveLayer == null)
				return string.Empty;

			int minX = CanvasWidth, maxX = -1, minY = CanvasHeight, maxY = -1;
			var pixels = ActiveLayer.Pixels;
			for (int y = 0; y < CanvasHeight; y++)
			{
				for (int x = 0; x < CanvasWidth; x++)
				{
					int idx = (y * CanvasWidth + x) * 4;
					if (pixels[idx + 3] > 0)
					{
						if (x < minX) minX = x;
						if (x > maxX) maxX = x;
						if (y < minY) minY = y;
						if (y > maxY) maxY = y;
					}
				}
			}

			if (maxX < minX || maxY < minY)
				return string.Empty;

			var sb = new System.Text.StringBuilder();
			double zoom = ZoomLevel;

			double left = minX * zoom;
			double top = minY * zoom;
			double right = (maxX + 1) * zoom;
			double bottom = (maxY + 1) * zoom;

			sb.Append($"M {left},{top} L {right},{top} L {right},{bottom} L {left},{bottom} Z");
			return sb.ToString();
		}

		private void NotifyOutlinePropertiesChanged()
		{
			OnPropertyChanged(nameof(HoverOutlinePathData));
			OnPropertyChanged(nameof(SelectionOutlinePathData));
			OnPropertyChanged(nameof(ActiveLayerOutlinePathData));
		}

		[RelayCommand]
		private void SelectSelectTool() => ActiveTool = PaintTool.Select;

		[RelayCommand]
		private void SelectBrushTool() => ActiveTool = PaintTool.Brush;

		[RelayCommand]
		private void SelectEraserTool() => ActiveTool = PaintTool.Eraser;

		[RelayCommand]
		private void SelectPickerTool() => ActiveTool = PaintTool.Picker;

		[RelayCommand]
		private void SelectBucketTool() => ActiveTool = PaintTool.Bucket;

		[RelayCommand]
		private void SelectWandTool() => ActiveTool = PaintTool.Wand;

		[RelayCommand]
		private void SelectMoveTool() => ActiveTool = PaintTool.Move;
	}
}
