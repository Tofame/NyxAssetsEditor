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
		Wand
	}

	public enum BrushShape
	{
		Square,
		Circle
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

		[ObservableProperty]
		private int _hoverX = -1;

		[ObservableProperty]
		private int _hoverY = -1;

		[ObservableProperty]
		private bool _isHovering;

		partial void OnBrushShapeChanged(BrushShape value)
		{
			OnPropertyChanged(nameof(IsSquareBrush));
			OnPropertyChanged(nameof(IsCircleBrush));
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

		public double ZoomDimension => 32 * ZoomLevel;

		[ObservableProperty]
		private Color _gradientEndColor = Colors.White;

		partial void OnZoomLevelChanged(double value)
		{
			OnPropertyChanged(nameof(ZoomDimension));
			NotifyOutlinePropertiesChanged();
		}

		[ObservableProperty]
		private PaletteViewModel? _selectedPalette;

		partial void OnSelectedPaletteChanged(PaletteViewModel? value) => ApplySelectedPalette();

		private readonly bool[,] _selectionMask = new bool[32, 32];
		private static readonly string PalettesFilePath = Path.Combine(AppContext.BaseDirectory, "Assets", "paint", "paint_palletes.toml");
		private DateTime _lastStateSave = DateTime.MinValue;

		public PaintViewModel(MainWindowViewModel mainWindow)
		{
			_mainWindow = mainWindow;
			LoadDefaultPalettes();
		}

		public void InitializeWithSprite(SpriteViewModel sprite, FloatingSpriteLoaderViewModel panel)
		{
			Sprite = sprite;
			Panel = panel;

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
			for (int y = 0; y < 32; y++)
			{
				for (int x = 0; x < 32; x++)
				{
					_selectionMask[x, y] = false;
				}
			}
			HasSelection = false;
			UpdateCanvasPreview();
		}

		public bool IsSelected(int x, int y)
		{
			if (x < 0 || x >= 32 || y < 0 || y >= 32)
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

		public void HandleCanvasClick(int x, int y, bool isRightClick)
		{
			if (ActiveLayer == null)
				return;

			if (x < 0 || x >= 32 || y < 0 || y >= 32)
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
					ApplyWandSelection(x, y);
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

					if (x >= 0 && x < 32 && y >= 0 && y < 32)
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
			return (dx * dx + dy * dy) <= (radius * radius);
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

			// Copy on Axis X (mirrors horizontal: x -> 31 - x)
			if (CopyOnAxisX)
			{
				int mx = 31 - x;
				if (mx >= 0 && mx < 32 && (!HasSelection || _selectionMask[mx, y]))
				{
					SetPixel(ActiveLayer.Pixels, mx, y, color);
				}
			}

			// Copy on Axis Y (mirrors vertical: y -> 31 - y)
			if (CopyOnAxisY)
			{
				int my = 31 - y;
				if (my >= 0 && my < 32 && (!HasSelection || _selectionMask[x, my]))
				{
					SetPixel(ActiveLayer.Pixels, x, my, color);
				}
			}

			// Both
			if (CopyOnAxisX && CopyOnAxisY)
			{
				int mx = 31 - x;
				int my = 31 - y;
				if (mx >= 0 && mx < 32 && my >= 0 && my < 32 && (!HasSelection || _selectionMask[mx, my]))
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

			var visited = new bool[32, 32];
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

					if (nx >= 0 && nx < 32 && ny >= 0 && ny < 32 && !visited[nx, ny])
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

		private void ApplyWandSelection(int startX, int startY)
		{
			if (ActiveLayer == null)
				return;

			ClearSelection();

			var pixels = ActiveLayer.Pixels;
			var targetColor = GetPixelColor(pixels, startX, startY);

			var queue = new Queue<(int, int)>();
			queue.Enqueue((startX, startY));

			_selectionMask[startX, startY] = true;
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

					if (nx >= 0 && nx < 32 && ny >= 0 && ny < 32 && !_selectionMask[nx, ny])
					{
						var color = GetPixelColor(pixels, nx, ny);
						if (ColorsAreSimilar(targetColor, color, FillThreshold))
						{
							_selectionMask[nx, ny] = true;
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
			int idx = (y * 32 + x) * 4;
			return Color.FromArgb(pixels[idx + 3], pixels[idx], pixels[idx + 1], pixels[idx + 2]);
		}

		private void SetPixel(byte[] pixels, int x, int y, Color color)
		{
			int idx = (y * 32 + x) * 4;
			pixels[idx] = color.R;
			pixels[idx + 1] = color.G;
			pixels[idx + 2] = color.B;
			pixels[idx + 3] = color.A;
		}

		public void UpdateCanvasPreview()
		{
			var composite = GetCompositePixels();
			var overlay = new byte[32 * 32 * 4];
			Array.Copy(composite, overlay, composite.Length);

			if (HasSelection)
			{
				for (int y = 0; y < 32; y++)
				{
					for (int x = 0; x < 32; x++)
					{
						if (_selectionMask[x, y])
						{
							int idx = (y * 32 + x) * 4;
							overlay[idx] = (byte)Math.Clamp(overlay[idx] + 40, 0, 255);
							overlay[idx + 1] = (byte)Math.Clamp(overlay[idx + 1] + 40, 0, 255);
							overlay[idx + 2] = (byte)Math.Clamp(overlay[idx + 2] + 100, 0, 255);
						}
					}
				}
			}

			if (CopyOnAxisX)
			{
				for (int y = 0; y < 32; y++)
				{
					BlendGuidePixel(overlay, 15, y, Colors.Red);
					BlendGuidePixel(overlay, 16, y, Colors.Red);
				}
			}

			if (CopyOnAxisY)
			{
				for (int x = 0; x < 32; x++)
				{
					BlendGuidePixel(overlay, x, 15, Colors.Red);
					BlendGuidePixel(overlay, x, 16, Colors.Red);
				}
			}

			if (IsHovering && HoverX >= 0 && HoverX < 32 && HoverY >= 0 && HoverY < 32)
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

							if (px >= 0 && px < 32 && py >= 0 && py < 32)
							{
								int idx = (py * 32 + px) * 4;
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

					var visited = new bool[32, 32];
					visited[HoverX, HoverY] = true;

					double alpha = 0.70;

					while (queue.Count > 0)
					{
						var (cx, cy) = queue.Dequeue();
						if (HasSelection && !_selectionMask[cx, cy])
							continue;

						int idx = (cy * 32 + cx) * 4;
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

							if (nx >= 0 && nx < 32 && ny >= 0 && ny < 32 && !visited[nx, ny])
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

			CanvasPreview = _renderer.Convert(overlay);

			var _now = DateTime.UtcNow;
			if (Sprite != null && (_now - _lastStateSave).TotalMilliseconds >= 500)
			{
				_lastStateSave = _now;
				PersistenceService.SavePaintState(this);
			}
		}

		private void BlendGuidePixel(byte[] pixels, int x, int y, Color guideColor)
		{
			int idx = (y * 32 + x) * 4;
			double alpha = 0.40;
			pixels[idx] = (byte)(pixels[idx] * (1.0 - alpha) + guideColor.R * alpha);
			pixels[idx + 1] = (byte)(pixels[idx + 1] * (1.0 - alpha) + guideColor.G * alpha);
			pixels[idx + 2] = (byte)(pixels[idx + 2] * (1.0 - alpha) + guideColor.B * alpha);
		}

		public byte[] GetCompositePixels()
		{
			var composite = new byte[32 * 32 * 4];

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
			var emptyPixels = new byte[32 * 32 * 4];
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
			for (int y = 0; y < 32; y++)
			{
				for (int x = 0; x < 16; x++)
				{
					int targetX = 31 - x;
					if (HasSelection && (!_selectionMask[x, y] || !_selectionMask[targetX, y]))
						continue;

					var temp = GetPixelColor(pixels, x, y);
					SetPixel(pixels, x, y, GetPixelColor(pixels, targetX, y));
					SetPixel(pixels, targetX, y, temp);
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
			for (int y = 0; y < 16; y++)
			{
				int targetY = 31 - y;
				for (int x = 0; x < 32; x++)
				{
					if (HasSelection && (!_selectionMask[x, y] || !_selectionMask[x, targetY]))
						continue;

					var temp = GetPixelColor(pixels, x, y);
					SetPixel(pixels, x, y, GetPixelColor(pixels, x, targetY));
					SetPixel(pixels, x, targetY, temp);
				}
			}

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
			for (int y = 0; y < 32; y++)
			{
				for (int x = 0; x < 32; x++)
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
		private void ZoomIn()
		{
			if (ZoomLevel < 24.0)
			{
				ZoomLevel += 2.0;
			}
		}

		[RelayCommand]
		private void ZoomOut()
		{
			if (ZoomLevel > 4.0)
			{
				ZoomLevel -= 2.0;
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

		[RelayCommand]
		private void Save()
		{
			if (Sprite != null && Panel != null)
			{
				Panel.ReplaceSpritePixels(new[] { Sprite }, GetCompositePixels());
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
			var pixels = GetCompositePixels();
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

			Layers.Clear();
			foreach (var layerModel in state.Layers)
			{
				byte[] pixels;
				try { pixels = Convert.FromBase64String(layerModel.Pixels ?? ""); }
				catch { pixels = new byte[32 * 32 * 4]; }
				if (pixels.Length != 32 * 32 * 4)
					pixels = new byte[32 * 32 * 4];

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

		private void SavePalettes()
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
			public List<(string Name, bool IsVisible, double Opacity, byte[] Pixels)> Layers { get; set; } = new();
		}

		private readonly Stack<PaintHistoryState> _undoStack = new();
		private readonly Stack<PaintHistoryState> _redoStack = new();

		private PaintHistoryState CaptureHistoryState()
		{
			var state = new PaintHistoryState();
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
			foreach (var l in Layers) UnsubscribeLayer(l);

			while (Layers.Count > state.Layers.Count)
			{
				Layers.RemoveAt(Layers.Count - 1);
			}
			while (Layers.Count < state.Layers.Count)
			{
				Layers.Add(new LayerViewModel("", new byte[32 * 32 * 4]));
			}

			for (int i = 0; i < state.Layers.Count; i++)
			{
				var target = Layers[i];
				var source = state.Layers[i];
				target.Name = source.Name;
				target.IsVisible = source.IsVisible;
				target.Opacity = source.Opacity;
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
				if (HoverX >= 0 && HoverX < 32 && HoverY >= 0 && HoverY < 32)
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

					if (px >= 0 && px < 32 && py >= 0 && py < 32)
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
						if (!IsWithinBrushShape(dx + 1, dy, radius) || px == 31)
						{
							sb.Append($"M {right},{top} L {right},{bottom} ");
						}
						// Top edge
						if (!IsWithinBrushShape(dx, dy - 1, radius) || py == 0)
						{
							sb.Append($"M {left},{top} L {right},{top} ");
						}
						// Bottom edge
						if (!IsWithinBrushShape(dx, dy + 1, radius) || py == 31)
						{
							sb.Append($"M {left},{bottom} L {right},{bottom} ");
						}
					}
				}
			}
			return sb.ToString();
		}

		private void NotifyOutlinePropertiesChanged()
		{
			OnPropertyChanged(nameof(HoverOutlinePathData));
		}
	}
}
