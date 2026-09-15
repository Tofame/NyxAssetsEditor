using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NyxAssets.Sprites;
using NyxAssets.Things;
using NyxAssets.Things.Frames;
using NyxAssetsEditor.Services.Archive;
using NyxAssetsEditor.Services.Persistence;
using NyxAssetsEditor.Services.Rendering;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;

namespace NyxAssetsEditor.ViewModels.Dialogs;

/// <summary>
/// Light preview dialog. Renders a small square scene (ground + wall "house" + lamp)
/// and applies OTC-faithful light: per-tile light buffer, distance falloff
/// factor = (-dist + level) * 0.2, max-blended with global light, multiply-composited
/// over the scene (see OTC LightView::draw).
/// </summary>
public sealed class LightPreviewDialogViewModel : INotifyPropertyChanged
{
	private readonly FloatingThingEditorViewModel _editor;
	private readonly SpriteRenderer _bitmapRenderer = new();
	private readonly DispatcherTimer _animationTimer;

	private WriteableBitmap? _previewImage;
	private uint _groundId = 103;
	private int _outerSize = 13;
	private int _houseSize = 7;
	private uint _poleId = 1102;
	private uint _horizontalId = 1103;
	private uint _verticalId = 1105;
	private uint _cornerId = 1104;
	private uint _lampId = 1424;
	private int _lightColor = 215;
	private int _lightLevel = 100;
	private int _globalColor = 215;   // server always sends white (from8bit * intensity/255)
	private int _globalIntensity = 40; // night default (LIGHT_NIGHT at 20:00)
	private int _lightViewIntensity = 100; // OTC setting in Interface (0-100%)
	private bool _animate = true;
	private bool _lightMapOnly = false;
	private int _lampFrame = 0;
	private string? _statusMessage;

	public event PropertyChangedEventHandler? PropertyChanged;

	public ObservableCollection<uint> AvailableGroundIds { get; } = new();

	// ---------- Scene ----------

	public uint GroundId
	{
		get => _groundId;
		set
		{
			if (_groundId != value)
			{
				_groundId = value;
				_groundIdText = value.ToString();
				OnPropertyChanged();
				OnPropertyChanged(nameof(GroundIdText));
				RefreshPreview();
			}
		}
	}

	private string _groundIdText = "103";
	public string GroundIdText
	{
		get => _groundIdText;
		set
		{
			if (_groundIdText != value)
			{
				_groundIdText = value;
				OnPropertyChanged();
				if (uint.TryParse(value?.Trim(), out var parsedId))
				{
					if (_groundId != parsedId)
					{
						_groundId = parsedId;
						OnPropertyChanged(nameof(GroundId));
						RefreshPreview();
					}
				}
			}
		}
	}

	public int OuterSize
	{
		get => _outerSize;
		set
		{
			int odd = value < 3 ? 3 : (value > 25 ? 25 : value);
			if (odd % 2 == 0) odd--; // keep odd so house centers exactly
			if (_outerSize != odd)
			{
				_outerSize = odd;
				OnPropertyChanged();
				OnPropertyChanged(nameof(MaxHouseSize));
				if (_houseSize > _outerSize - 2)
				{
					HouseSize = Math.Max(3, _outerSize - 2);
					return;
				}
				RefreshPreview();
			}
		}
	}

	public int MaxHouseSize => Math.Max(3, _outerSize - 2);

	public int HouseSize
	{
		get => _houseSize;
		set
		{
			int max = Math.Max(3, _outerSize - 2);
			int odd = value < 3 ? 3 : (value > max ? max : value);
			if (odd % 2 == 0) odd--;
			if (_houseSize != odd)
			{
				_houseSize = odd;
				OnPropertyChanged();
				RefreshPreview();
			}
		}
	}

	// ---------- Walls ----------

	public uint MaxItemId
	{
		get
		{
			var catalog = _editor.SourcePanel.Catalog;
			return catalog == null ? 65535 : Math.Max(1, catalog.ItemCount);
		}
	}

	public uint PoleId
	{
		get => _poleId;
		set { if (_poleId != value) { _poleId = value; OnPropertyChanged(); RefreshPreview(); } }
	}

	public uint HorizontalId
	{
		get => _horizontalId;
		set { if (_horizontalId != value) { _horizontalId = value; OnPropertyChanged(); RefreshPreview(); } }
	}

	public uint VerticalId
	{
		get => _verticalId;
		set { if (_verticalId != value) { _verticalId = value; OnPropertyChanged(); RefreshPreview(); } }
	}

	public uint CornerId
	{
		get => _cornerId;
		set { if (_cornerId != value) { _cornerId = value; OnPropertyChanged(); RefreshPreview(); } }
	}

	// ---------- Lamp ----------

	public uint LampId
	{
		get => _lampId;
		set { if (_lampId != value) { _lampId = value; OnPropertyChanged(); RefreshPreview(); } }
	}

	private bool _isSyncingWithEditor;

	public int LightColor
	{
		get => _lightColor;
		set
		{
			if (_lightColor != value)
			{
				_lightColor = Math.Clamp(value, 0, 215);
				OnPropertyChanged();
				OnPropertyChanged(nameof(LightColorBrush));
				if (!_isSyncingWithEditor)
				{
					_isSyncingWithEditor = true;
					try
					{
						_editor.HasLight = true;
						_editor.LightColor = (uint)_lightColor;
					}
					finally
					{
						_isSyncingWithEditor = false;
					}
				}
				RefreshPreview();
			}
		}
	}

	public int LightLevel
	{
		get => _lightLevel;
		set
		{
			if (_lightLevel != value)
			{
				_lightLevel = Math.Clamp(value, 0, 255);
				OnPropertyChanged();
				if (!_isSyncingWithEditor)
				{
					_isSyncingWithEditor = true;
					try
					{
						_editor.HasLight = true;
						_editor.LightLevel = (uint)_lightLevel;
					}
					finally
					{
						_isSyncingWithEditor = false;
					}
				}
				RefreshPreview();
			}
		}
	}

	public IBrush LightColorBrush => new SolidColorBrush(FloatingThingEditorViewModel.Get8BitColor(_lightColor));

	public IBrush GlobalColorBrush => new SolidColorBrush(FloatingThingEditorViewModel.Get8BitColor(_globalColor));

	public IReadOnlyList<FloatingThingEditorViewModel.PaletteColor> PaletteColors =>
		FloatingThingEditorViewModel.SharedPaletteColors;

	// ---------- Time of Day (drives global light) ----------

	private int _timeMinutes = 1200; // default: night (20:00)

	/// <summary>Tibia time in minutes (0..1439). 720 = noon, 1200 = full night.</summary>
	public int TimeMinutes
	{
		get => _timeMinutes;
		set
		{
			int t = Math.Clamp(value, 0, 1439);
			if (_timeMinutes != t)
			{
				_timeMinutes = t;
				OnPropertyChanged();
				OnPropertyChanged(nameof(TimeDisplay));
				ApplyTimeToGlobalLight();
			}
		}
	}

	public string TimeDisplay => $"{_timeMinutes / 60:00}:{_timeMinutes % 60:00}";

	public void SetDay() => TimeMinutes = 720;

	public void SetNight() => TimeMinutes = 1200;

	/// <summary>
	/// Server-faithful world light curve (WosOts Game::updateWorldLightLevel):
	/// LIGHT_DAY=250, LIGHT_NIGHT=40; sunrise ramp 360..480 (rate 1.75), day to 1080,
	/// dusk ramp 1080..1200 (rate 1.75), night otherwise. Server always sends color 215
	/// with level 40..250; client multiplies from8bit(215)=white by intensity/255.
	/// </summary>
	private void ApplyTimeToGlobalLight()
	{
		const int day = 250, night = 40;
		int t = _timeMinutes;
		float level;
		if (t >= 360 && t <= 480)
			level = (t - 360) * 1.75f + night;      // sunrise ramp up
		else if (t > 480 && t < 1080)
			level = day;                            // full day
		else if (t >= 1080 && t <= 1200)
			level = day - (t - 1080) * 1.75f;      // dusk ramp down
		else
			level = night;                          // full night

		_isApplyingTime = true;
		try
		{
			GlobalIntensity = Math.Clamp((int)level, 0, 255);
		}
		finally
		{
			_isApplyingTime = false;
		}
	}

	// ---------- Global Light (server light simulation) ----------

	public int GlobalColor
	{
		get => _globalColor;
		set
		{
			if (_globalColor != value)
			{
				_globalColor = Math.Clamp(value, 0, 215);
				OnPropertyChanged();
				OnPropertyChanged(nameof(GlobalColorBrush));
				RefreshPreview();
			}
		}
	}

	private bool _isApplyingTime;

	public int GlobalIntensity
	{
		get => _globalIntensity;
		set
		{
			int clamped = Math.Clamp(value, 0, 255);
			if (_globalIntensity != clamped)
			{
				_globalIntensity = clamped;
				OnPropertyChanged();
				if (!_isApplyingTime)
				{
					// Map intensity back to time
					const int day = 250, night = 40;
					int approxMinutes;
					if (clamped <= night)
					{
						approxMinutes = 1200; // full night (20:00)
					}
					else if (clamped >= day)
					{
						approxMinutes = 720;  // full day (12:00)
					}
					else
					{
						// In dusk ramp (1080..1200): level = day - (t - 1080) * 1.75f => t = 1080 + (day - level) / 1.75f
						approxMinutes = (int)Math.Round(1080 + (day - clamped) / 1.75f);
					}
					_timeMinutes = Math.Clamp(approxMinutes, 0, 1439);
					OnPropertyChanged(nameof(TimeMinutes));
					OnPropertyChanged(nameof(TimeDisplay));
				}
				RefreshPreview();
			}
		}
	}

	/// <summary>OTC setting in Interface (0-100%). Modulates the entire light view.</summary>
	public int LightViewIntensity
	{
		get => _lightViewIntensity;
		set
		{
			int clamped = Math.Clamp(value, 0, 100);
			if (_lightViewIntensity != clamped)
			{
				_lightViewIntensity = clamped;
				OnPropertyChanged();
				RefreshPreview();
			}
		}
	}

	// ---------- Options ----------

	public bool Animate
	{
		get => _animate;
		set
		{
			if (_animate != value)
			{
				_animate = value;
				OnPropertyChanged();
				if (!value)
				{
					_lampFrame = 0;
					RefreshPreview();
				}
			}
		}
	}

	public bool LightMapOnly
	{
		get => _lightMapOnly;
		set
		{
			if (_lightMapOnly != value)
			{
				_lightMapOnly = value;
				OnPropertyChanged();
				RefreshPreview();
			}
		}
	}

	// ---------- Output ----------

	public WriteableBitmap? PreviewImage
	{
		get => _previewImage;
		private set
		{
			_previewImage = value;
			OnPropertyChanged();
		}
	}

	public string? StatusMessage
	{
		get => _statusMessage;
		private set
		{
			_statusMessage = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasStatusMessage));
		}
	}

	public bool HasStatusMessage => !string.IsNullOrEmpty(_statusMessage);

	// ---------- Lifecycle ----------

	public LightPreviewDialogViewModel(FloatingThingEditorViewModel editor)
	{
		_editor = editor;

		// Load persisted scene state (size props clamp to valid odd ranges)
		var state = PersistenceService.GetLightPreviewState();
		_groundId = state.GroundId == 0 ? 103 : state.GroundId;
		_groundIdText = _groundId.ToString();
		OuterSize = state.OuterSize;
		HouseSize = state.HouseSize;
		_poleId = state.PoleId == 0 ? 1102 : state.PoleId;
		_horizontalId = state.HorizontalId == 0 ? 1103 : state.HorizontalId;
		_verticalId = state.VerticalId == 0 ? 1105 : state.VerticalId;
		_cornerId = state.CornerId == 0 ? 1104 : state.CornerId;
		_lampId = editor.IsItem ? editor.ThingId : (state.LampId == 0 ? 1424 : state.LampId);

		// Light values: editor thing first, then persisted, then sensible defaults
		var thing = editor.Thing;
		if (thing != null && thing.HasLight)
		{
			_lightColor = (int)thing.LightColor;
			_lightLevel = (int)thing.LightLevel;
		}
		else if (state.LightLevel > 0)
		{
			_lightColor = state.LightColor;
			_lightLevel = state.LightLevel;
		}

		_timeMinutes = state.TimeMinutes;
		_globalIntensity = state.GlobalIntensity;
		_globalColor = state.GlobalColor;
		_lightViewIntensity = Math.Clamp(state.LightViewIntensity, 0, 100);
		_animate = state.Animate;
		_lightMapOnly = state.LightMapOnly;

		_animationTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(150),
		};
		_animationTimer.Tick += OnAnimationTick;

		PopulateChoices();
		RefreshPreview();
		_editor.PropertyChanged += OnEditorPropertyChanged;
		_animationTimer.Start();
	}

	private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (_isSyncingWithEditor)
			return;

		if (e.PropertyName == nameof(FloatingThingEditorViewModel.LightColor))
		{
			_isSyncingWithEditor = true;
			try
			{
				_lightColor = (int)_editor.LightColor;
				OnPropertyChanged(nameof(LightColor));
				OnPropertyChanged(nameof(LightColorBrush));
				RefreshPreview();
			}
			finally
			{
				_isSyncingWithEditor = false;
			}
		}
		else if (e.PropertyName == nameof(FloatingThingEditorViewModel.LightLevel))
		{
			_isSyncingWithEditor = true;
			try
			{
				_lightLevel = (int)_editor.LightLevel;
				OnPropertyChanged(nameof(LightLevel));
				RefreshPreview();
			}
			finally
			{
				_isSyncingWithEditor = false;
			}
		}
	}

	public void StopTimer()
	{
		_editor.PropertyChanged -= OnEditorPropertyChanged;
		_animationTimer.Stop();
		SaveState();
	}

	private void PopulateChoices()
	{
		var catalog = _editor.SourcePanel.Catalog;
		if (catalog == null)
			return;

		foreach (var item in catalog.EnumerateItems())
		{
			if (item.IsGround)
				AvailableGroundIds.Add(item.Id);
		}

		if (AvailableGroundIds.Count == 0)
		{
			foreach (var anyItem in catalog.EnumerateItems().Take(2))
				AvailableGroundIds.Add(anyItem.Id);
		}
	}

	private void OnAnimationTick(object? sender, EventArgs e)
	{
		if (!_animate)
			return;

		var catalog = _editor.SourcePanel.Catalog;
		if (catalog == null)
			return;

		var lamp = catalog.TryGetItem(_lampId);
		if (lamp == null || lamp.FrameGroups.Count == 0)
			return;

		var fg = lamp.FrameGroups[0];
		if (fg.Frames > 1)
		{
			_lampFrame = (_lampFrame + 1) % (int)fg.Frames;
			RefreshPreview();
		}
	}

	// ---------- Actions ----------

	public void ApplyLightToEditor()
	{
		_editor.HasLight = true;
		_editor.LightColor = (uint)_lightColor;
		_editor.LightLevel = (uint)_lightLevel;
		StatusMessage = $"Applied Light (color {_lightColor}, level {_lightLevel}) to Thing #{_editor.ThingId}.";
		SaveState();
	}

	public void ResetLight()
	{
		var thing = _editor.Thing;
		if (thing != null && thing.HasLight)
		{
			_lightColor = (int)thing.LightColor;
			_lightLevel = (int)thing.LightLevel;
			StatusMessage = $"Reset to Thing #{_editor.ThingId} light (color {_lightColor}, level {_lightLevel}).";
		}
		else
		{
			_lightColor = 215;
			_lightLevel = 100;
			StatusMessage = "Thing has no light. Reset to defaults (color 215, level 100).";
		}
		OnPropertyChanged(nameof(LightColor));
		OnPropertyChanged(nameof(LightLevel));
		OnPropertyChanged(nameof(LightColorBrush));
		RefreshPreview();
	}

	public void SaveState()
	{
		PersistenceService.SaveLightPreviewState(new PersistenceService.LightPreviewStateModel
		{
			GroundId = _groundId,
			OuterSize = _outerSize,
			HouseSize = _houseSize,
			PoleId = _poleId,
			HorizontalId = _horizontalId,
			VerticalId = _verticalId,
			CornerId = _cornerId,
			LampId = _lampId,
			LightLevel = _lightLevel,
			LightColor = _lightColor,
			TimeMinutes = _timeMinutes,
			GlobalIntensity = _globalIntensity,
			GlobalColor = _globalColor,
			LightViewIntensity = _lightViewIntensity,
			Animate = _animate,
			LightMapOnly = _lightMapOnly
		});
	}

	// ---------- Rendering ----------

	public void RefreshPreview()
	{
		var catalog = _editor.SourcePanel.Catalog;
		var loader = _editor.SourcePanel.GetActiveSpriteLoader();
		if (catalog == null || loader == null)
		{
			PreviewImage = null;
			return;
		}

		const int edge = (int)SpritePixelCodec.SpriteEdgeLength; // 32
		const int padding = 16;

		int outer = _outerSize;
		int house = _houseSize;
		int h0 = (outer - house) / 2;
		int h1 = h0 + house - 1;
		int center = outer / 2;

		int canvasW = outer * edge + padding * 2;
		int canvasH = canvasW;
		var canvas = new byte[canvasW * canvasH * 4];

		// 1. Ground
		var ground = catalog.TryGetItem(_groundId);
		if (ground != null && ground.FrameGroups.Count > 0)
		{
			for (int ty = 0; ty < outer; ty++)
			{
				for (int tx = 0; tx < outer; tx++)
				{
					DrawItemOnCanvas(canvas, canvasW, canvasH, ground, 0, padding + tx * edge, padding + ty * edge, loader);
				}
			}
		}

		// 2. House walls (Tibia layout): pole upper-left corner, corner piece bottom-right,
		// horizontal along top/bottom edges, vertical on left/right sides
		void DrawWall(int tx, int ty)
		{
			int anchorX = padding + tx * edge;
			int anchorY = padding + ty * edge;

			uint wallId;
			if (tx == h0 && ty == h0)
				wallId = _poleId;          // pole: upper-left
			else if (tx == h1 && ty == h1)
				wallId = _cornerId;        // corner: bottom-right
			else if (tx == h0 && ty == h1)
				wallId = _verticalId;      // bottom-left corner connects vertically
			else if (ty == h0 || ty == h1)
				wallId = _horizontalId;    // top/bottom edges
			else
				wallId = _verticalId;      // left/right sides

			var wall = catalog.TryGetItem(wallId);
			if (wall != null && wall.FrameGroups.Count > 0)
				DrawItemOnCanvas(canvas, canvasW, canvasH, wall, 0, anchorX, anchorY, loader);
		}

		for (int ty = h0; ty <= h1; ty++)
		{
			for (int tx = h0; tx <= h1; tx++)
			{
				if (tx == h0 || tx == h1 || ty == h0 || ty == h1)
					DrawWall(tx, ty);
			}
		}

		// 3. Lamp in the center
		var lamp = catalog.TryGetItem(_lampId);
		if (lamp != null && lamp.FrameGroups.Count > 0)
			DrawItemOnCanvas(canvas, canvasW, canvasH, lamp, _lampFrame, padding + center * edge, padding + center * edge, loader);

		// 4. Light (OTC LightView: per-tile buffer, multiply blend)
		ApplyLight(canvas, canvasW, canvasH, outer, edge, padding, center);

		var old = _previewImage;
		PreviewImage = _bitmapRenderer.ConvertRgba(canvasW, canvasH, canvas);
		old?.Dispose();
	}

	private void ApplyLight(byte[] canvas, int canvasW, int canvasH, int outer, int edge, int padding, int center)
	{
		// OTC ambient light (Options -> Interface -> Ambient Light 0..100%):
		// In OTC (mapview.cpp): ambientLight.intensity = std::max(m_minimumAmbientLight * 255, ambientLight.intensity)
		// It sets a minimum brightness floor for the ambient/global darkness.
		int minAmbient = (int)(_lightViewIntensity * 255f / 100f);
		int effectiveGlobalIntensity = Math.Max(minAmbient, _globalIntensity);

		// Global light (server light): from8bit(color) * intensity/255
		int gr, gg, gb;
		From8Bit(_globalColor, out gr, out gg, out gb);
		gr = (int)(gr * effectiveGlobalIntensity / 255f);
		gg = (int)(gg * effectiveGlobalIntensity / 255f);
		gb = (int)(gb * effectiveGlobalIntensity / 255f);

		int lr, lg, lb;
		From8Bit(_lightColor, out lr, out lg, out lb);

		float lampX = padding + center * edge + edge / 2f;
		float lampY = lampX;

		// Per-tile light color (blocky, exactly like OTC: 1 light texel per tile)
		var tileLight = new byte[outer * outer * 3];
		for (int ty = 0; ty < outer; ty++)
		{
			for (int tx = 0; tx < outer; tx++)
			{
				float px = padding + tx * edge + edge / 2f;
				float py = padding + ty * edge + edge / 2f;

				float dist = (float)Math.Sqrt((px - lampX) * (px - lampX) + (py - lampY) * (py - lampY)) / edge;
				float factor = (-dist + _lightLevel) * 0.2f;

				int r = gr, g = gg, b = gb;
				if (factor > 0.01f)
				{
					if (factor > 1.0f) factor = 1.0f;
					r = Math.Max(r, (int)(lr * factor));
					g = Math.Max(g, (int)(lg * factor));
					b = Math.Max(b, (int)(lb * factor));
				}

				int li = (ty * outer + tx) * 3;
				tileLight[li] = (byte)Math.Clamp(r, 0, 255);
				tileLight[li + 1] = (byte)Math.Clamp(g, 0, 255);
				tileLight[li + 2] = (byte)Math.Clamp(b, 0, 255);
			}
		}

		if (_lightMapOnly)
		{
			// Show raw per-tile light texture
			for (int ty = 0; ty < outer; ty++)
			{
				for (int tx = 0; tx < outer; tx++)
				{
					int li = (ty * outer + tx) * 3;
					int r = tileLight[li];
					int g = tileLight[li + 1];
					int b = tileLight[li + 2];
					int x0 = padding + tx * edge, y0 = padding + ty * edge;
					for (int y = y0; y < y0 + edge; y++)
					{
						for (int x = x0; x < x0 + edge; x++)
						{
							int idx = (y * canvasW + x) * 4;
							canvas[idx] = (byte)r;
							canvas[idx + 1] = (byte)g;
							canvas[idx + 2] = (byte)b;
							canvas[idx + 3] = 255;
						}
					}
				}
			}
			return;
		}

		// Multiply-composite per-tile light over scene (OTC CompositionMode_Multiply)
		for (int ty = 0; ty < outer; ty++)
		{
			for (int tx = 0; tx < outer; tx++)
			{
				int li = (ty * outer + tx) * 3;
				float fr = tileLight[li] / 255f;
				float fg = tileLight[li + 1] / 255f;
				float fb = tileLight[li + 2] / 255f;

				int x0 = padding + tx * edge, y0 = padding + ty * edge;
				for (int y = y0; y < y0 + edge; y++)
				{
					int rowBase = y * canvasW;
					for (int x = x0; x < x0 + edge; x++)
					{
						int idx = (rowBase + x) * 4;
						canvas[idx] = (byte)(canvas[idx] * fr);
						canvas[idx + 1] = (byte)(canvas[idx + 1] * fg);
						canvas[idx + 2] = (byte)(canvas[idx + 2] * fb);
					}
				}
			}
		}
	}

	/// <summary>OTC Color::from8bit (Tibia 216-color palette).</summary>
	private static void From8Bit(int color, out int r, out int g, out int b)
	{
		if (color <= 0 || color >= 216)
		{
			r = g = b = 0;
			return;
		}
		r = (color / 36) % 6 * 51;
		g = (color / 6) % 6 * 51;
		b = (color % 6) * 51;
	}

	// ---------- Sprite drawing (same pipeline as OffsetPreviewDialogViewModel) ----------

	private static void DrawItemOnCanvas(byte[] canvas, int canvasW, int canvasH, ThingType item, int frame, int anchorX, int anchorY, SpriteLoader loader)
	{
		if (item.FrameGroups.Count == 0)
			return;

		try
		{
			var selection = ThingFrameResolver.GetItemFrame(item, new ItemFrameRequest { Frame = (uint)frame });
			DrawFrameSelection(canvas, canvasW, canvasH, selection, anchorX, anchorY, 0, 0, loader);
		}
		catch { }
	}

	private static void DrawFrameSelection(byte[] canvas, int canvasW, int canvasH, ThingFrameSelection selection, int anchorX, int anchorY, int dispX, int dispY, SpriteLoader loader)
	{
		var fg = selection.FrameGroup;
		const int edge = (int)SpritePixelCodec.SpriteEdgeLength; // 32

		int innerOriginX = anchorX - dispX - (int)((fg.Width - 1) * edge);
		int innerOriginY = anchorY - dispY - (int)((fg.Height - 1) * edge);

		foreach (var slot in selection.EnumerateSpriteSlots().OrderBy(s => s.Layer))
		{
			if (slot.SpriteId == 0)
				continue;

			byte[] pixels;
			try
			{
				pixels = loader.LoadSpritePixels(slot.SpriteId);
			}
			catch
			{
				continue;
			}

			int destX = innerOriginX + (int)((fg.Width - slot.InnerWidth - 1) * edge);
			int destY = innerOriginY + (int)((fg.Height - slot.InnerHeight - 1) * edge);

			BlitRgba(canvas, canvasW, canvasH, destX, destY, pixels, edge, edge);
		}
	}

	private static void BlitRgba(byte[] dst, int dstW, int dstH, int x, int y, byte[] src, int srcW, int srcH)
	{
		for (int sy = 0; sy < srcH; sy++)
		{
			int dy = y + sy;
			if (dy < 0 || dy >= dstH) continue;

			for (int sx = 0; sx < srcW; sx++)
			{
				int dx = x + sx;
				if (dx < 0 || dx >= dstW) continue;

				int srcIdx = (sy * srcW + sx) * 4;
				byte a = src[srcIdx + 3];
				if (a == 0) continue;

				int dstIdx = (dy * dstW + dx) * 4;
				byte r = src[srcIdx];
				byte g = src[srcIdx + 1];
				byte b = src[srcIdx + 2];

				if (a == 255)
				{
					dst[dstIdx] = r;
					dst[dstIdx + 1] = g;
					dst[dstIdx + 2] = b;
					dst[dstIdx + 3] = 255;
				}
				else
				{
					float alpha = a / 255f;
					float invAlpha = 1f - alpha;
					dst[dstIdx] = (byte)(r * alpha + dst[dstIdx] * invAlpha);
					dst[dstIdx + 1] = (byte)(g * alpha + dst[dstIdx + 1] * invAlpha);
					dst[dstIdx + 2] = (byte)(b * alpha + dst[dstIdx + 2] * invAlpha);
					dst[dstIdx + 3] = (byte)Math.Min(255, dst[dstIdx + 3] + a);
				}
			}
		}
	}

	private void OnPropertyChanged([CallerMemberName] string? name = null) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
