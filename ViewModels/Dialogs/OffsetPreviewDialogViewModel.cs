using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NyxAssets.Sprites;
using NyxAssets.Things;
using NyxAssets.Things.Frames;
using NyxAssetsEditor.Services.Archive;
using NyxAssetsEditor.Services.Rendering;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;
using NyxAssetsEditor.ViewModels.Pages;

namespace NyxAssetsEditor.ViewModels.Dialogs;

public sealed class OffsetPreviewDialogViewModel : INotifyPropertyChanged
{
	private readonly FloatingThingEditorViewModel _editor;
	private readonly SpriteRenderer _bitmapRenderer = new();
	private readonly DispatcherTimer _animationTimer;

	private WriteableBitmap? _previewImage;
	private uint? _selectedGroundId;
	private uint? _selectedOutfitId;
	private Direction4 _outfitDirection = Direction4.South;
	private bool _isWalkingAnimation = true;
	private int _animationPhase = 0;
	private int _effectFrame = 0;
	private int _customOffsetX;
	private int _customOffsetY;
	private bool _overrideOffset;
	private string? _statusMessage;

	public event PropertyChangedEventHandler? PropertyChanged;

	public string Title => $"In-Game Offset Preview — {_editor.Kind} #{_editor.ThingId}";

	public bool IsEffect => _editor.IsEffect;
	public bool IsOutfit => _editor.IsOutfit;
	public bool IsItem => _editor.IsItem;
	public bool IsMissile => _editor.IsMissile;
	public bool ShowReferenceOutfitControls => IsEffect;
	public bool ShowOutfitDirectionControls => IsOutfit || IsEffect;

	public ObservableCollection<uint> AvailableGroundIds { get; } = new();
	public ObservableCollection<uint> AvailableOutfitIds { get; } = new();

	public uint? SelectedGroundId
	{
		get => _selectedGroundId;
		set
		{
			if (_selectedGroundId != value)
			{
				_selectedGroundId = value;
				OnPropertyChanged();
				RefreshPreview();
			}
		}
	}

	public uint? SelectedOutfitId
	{
		get => _selectedOutfitId;
		set
		{
			if (_selectedOutfitId != value)
			{
				_selectedOutfitId = value;
				OnPropertyChanged();
				RefreshPreview();
			}
		}
	}

	public Direction4 OutfitDirection
	{
		get => _outfitDirection;
		set
		{
			if (_outfitDirection != value)
			{
				_outfitDirection = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(IsNorth));
				OnPropertyChanged(nameof(IsEast));
				OnPropertyChanged(nameof(IsSouth));
				OnPropertyChanged(nameof(IsWest));
				RefreshPreview();
			}
		}
	}

	public bool IsNorth => _outfitDirection == Direction4.North;
	public bool IsEast => _outfitDirection == Direction4.East;
	public bool IsSouth => _outfitDirection == Direction4.South;
	public bool IsWest => _outfitDirection == Direction4.West;

	public bool IsWalkingAnimation
	{
		get => _isWalkingAnimation;
		set
		{
			if (_isWalkingAnimation != value)
			{
				_isWalkingAnimation = value;
				OnPropertyChanged();
				if (!value)
				{
					_animationPhase = 0;
					OnPropertyChanged(nameof(AnimationPhase));
				}
				RefreshPreview();
			}
		}
	}

	public int AnimationPhase
	{
		get => _animationPhase;
		set
		{
			if (_animationPhase != value)
			{
				_animationPhase = value;
				OnPropertyChanged();
				RefreshPreview();
			}
		}
	}

	public int CustomOffsetX
	{
		get => _overrideOffset ? _customOffsetX : _editor.OffsetX;
		set
		{
			_overrideOffset = true;
			if (_customOffsetX != value)
			{
				_customOffsetX = value;
				OnPropertyChanged();
				RefreshPreview();
			}
		}
	}

	public int CustomOffsetY
	{
		get => _overrideOffset ? _customOffsetY : _editor.OffsetY;
		set
		{
			_overrideOffset = true;
			if (_customOffsetY != value)
			{
				_customOffsetY = value;
				OnPropertyChanged();
				RefreshPreview();
			}
		}
	}

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

	public OffsetPreviewDialogViewModel(FloatingThingEditorViewModel editor)
	{
		_editor = editor;
		_customOffsetX = editor.OffsetX;
		_customOffsetY = editor.OffsetY;

		_animationTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(150),
		};
		_animationTimer.Tick += OnAnimationTick;

		PopulateChoices();
		SettingsViewModel.OffsetPreviewSettingsChanged += OnSettingsChanged;
		RefreshPreview();
		_animationTimer.Start();
	}

	private void OnSettingsChanged()
	{
		RefreshPreview();
	}

	public void StopTimer()
	{
		_animationTimer.Stop();
		SettingsViewModel.OffsetPreviewSettingsChanged -= OnSettingsChanged;
	}

	public void SetDirection(Direction4 dir) => OutfitDirection = dir;

	public void ResetOffsetToEditor()
	{
		_customOffsetX = _editor.OffsetX;
		_customOffsetY = _editor.OffsetY;
		_overrideOffset = false;
		OnPropertyChanged(nameof(CustomOffsetX));
		OnPropertyChanged(nameof(CustomOffsetY));
		RefreshPreview();
	}

	public void ApplyOffsetToEditor()
	{
		_editor.HasOffset = true;
		_editor.OffsetX = CustomOffsetX;
		_editor.OffsetY = CustomOffsetY;
		StatusMessage = $"Applied Offset ({CustomOffsetX}, {CustomOffsetY}) to Thing #{_editor.ThingId}.";
	}

	private void PopulateChoices()
	{
		var catalog = _editor.SourcePanel.Catalog;
		if (catalog == null)
			return;

		// Populate Grounds: items with IsGround
		uint? firstGround = null;
		uint? secondGround = null;
		foreach (var item in catalog.EnumerateItems())
		{
			if (item.IsGround)
			{
				AvailableGroundIds.Add(item.Id);
				if (firstGround == null)
					firstGround = item.Id;
				else if (secondGround == null)
					secondGround = item.Id;
			}
		}

		if (AvailableGroundIds.Count == 0)
		{
			// Fallback: add first item if no ground flag found
			foreach (var anyItem in catalog.EnumerateItems().Take(2))
			{
				AvailableGroundIds.Add(anyItem.Id);
				if (firstGround == null)
					firstGround = anyItem.Id;
				else if (secondGround == null)
					secondGround = anyItem.Id;
			}
		}

		// Use the 2nd ground by default (fallback to 1st if only 1 exists)
		_selectedGroundId = secondGround ?? firstGround;

		// Populate Outfits
		uint? firstOutfit = null;
		foreach (var outfit in catalog.EnumerateOutfits())
		{
			AvailableOutfitIds.Add(outfit.Id);
			firstOutfit ??= outfit.Id;
		}

		_selectedOutfitId = firstOutfit;
	}

	private void OnAnimationTick(object? sender, EventArgs e)
	{
		if (!_isWalkingAnimation)
			return;

		var catalog = _editor.SourcePanel.Catalog;
		var loader = _editor.SourcePanel.GetActiveSpriteLoader();
		if (catalog == null || loader == null)
			return;

		bool changed = false;

		// 1. Advance outfit animation
		var activeOutfitThing = IsOutfit
			? _editor.Thing
			: (SelectedOutfitId.HasValue ? catalog.TryGetOutfit(SelectedOutfitId.Value) : null);

		if (activeOutfitThing != null && activeOutfitThing.FrameGroups.Count > 0)
		{
			var maxPhases = activeOutfitThing.FrameGroups.Count > 1
				? (int)(activeOutfitThing.FrameGroups[0].Frames + activeOutfitThing.FrameGroups[1].Frames)
				: (int)activeOutfitThing.FrameGroups[0].Frames;

			if (maxPhases > 1)
			{
				_animationPhase = (_animationPhase + 1) % maxPhases;
				OnPropertyChanged(nameof(AnimationPhase));
				changed = true;
			}
		}

		// 2. Advance effect/item/missile animation if target thing is not outfit
		if (!IsOutfit)
		{
			var targetThing = _editor.Thing;
			if (targetThing != null && targetThing.FrameGroups.Count > 0)
			{
				var targetFg = targetThing.FrameGroups[0];
				if (targetFg.Frames > 1)
				{
					_effectFrame = (_effectFrame + 1) % (int)targetFg.Frames;
					changed = true;
				}
			}
		}

		if (changed)
		{
			RefreshPreview();
		}
	}

	public void RefreshPreview()
	{
		var catalog = _editor.SourcePanel.Catalog;
		var loader = _editor.SourcePanel.GetActiveSpriteLoader();
		if (catalog == null || loader == null)
		{
			PreviewImage = null;
			return;
		}

		// Compute required grid span so all tiles of large outfits/effects fit comfortably
		const int edge = (int)SpritePixelCodec.SpriteEdgeLength; // 32
		const int padding = 16;

		int maxReachLeft = 0;
		int maxReachRight = 0;
		int maxReachUp = 0;
		int maxReachDown = 0;

		void MeasureThing(ThingType? t, int offX, int offY, bool isOutfitCentering)
		{
			if (t == null || t.FrameGroups.Count == 0) return;
			var fg = t.FrameGroups[0];
			int w = (int)Math.Max(1, fg.Width);
			int h = (int)Math.Max(1, fg.Height);

			int dispX = offX;
			int dispY = offY;
			if (isOutfitCentering && SettingsViewModel.OffsetPreviewCenterOutfits && !t.DontCenterOutfit && w > 1)
			{
				dispX -= (w - 1) * edge / 2;
			}

			// Horizontal reach relative to center tile [0, edge]
			// The drawing origin is: tileAnchorX - dispX - (w - 1) * edge
			// The drawing right is: originX + w * edge = tileAnchorX - dispX + edge
			int minX = -dispX - (w - 1) * edge;
			int maxX = -dispX + edge;

			int minY = -dispY - (h - 1) * edge;
			int maxY = -dispY + edge;

			if (minX < 0) maxReachLeft = Math.Max(maxReachLeft, -minX);
			if (maxX > edge) maxReachRight = Math.Max(maxReachRight, maxX - edge);
			if (minY < 0) maxReachUp = Math.Max(maxReachUp, -minY);
			if (maxY > edge) maxReachDown = Math.Max(maxReachDown, maxY - edge);
		}

		var targetThing = _editor.Thing;
		var effOffsetX = CustomOffsetX;
		var effOffsetY = CustomOffsetY;

		if (IsOutfit && targetThing != null)
		{
			MeasureThing(targetThing, effOffsetX, effOffsetY, isOutfitCentering: true);
		}
		else if (IsEffect)
		{
			if (SelectedOutfitId.HasValue)
			{
				var refOutfit = catalog.TryGetOutfit(SelectedOutfitId.Value);
				if (refOutfit != null)
				{
					int refOffX = refOutfit.HasOffset ? refOutfit.OffsetX : 0;
					int refOffY = refOutfit.HasOffset ? refOutfit.OffsetY : 0;
					MeasureThing(refOutfit, refOffX, refOffY, isOutfitCentering: true);
				}
			}
			if (targetThing != null)
			{
				MeasureThing(targetThing, effOffsetX, effOffsetY, isOutfitCentering: false);
			}
		}
		else if (targetThing != null)
		{
			MeasureThing(targetThing, effOffsetX, effOffsetY, isOutfitCentering: false);
		}

		int tilesNeededHorizontal = 1 + 2 * (int)Math.Ceiling(Math.Max(maxReachLeft, maxReachRight) / (double)edge);
		int tilesNeededVertical = 1 + 2 * (int)Math.Ceiling(Math.Max(maxReachUp, maxReachDown) / (double)edge);

		int gridTiles = Math.Max(3, Math.Max(tilesNeededHorizontal, tilesNeededVertical));
		if (gridTiles % 2 == 0) gridTiles++; // Ensure odd number of tiles so middle tile is exact center

		const int maxTiles = 9;
		if (gridTiles > maxTiles) gridTiles = maxTiles;

		int centerTileIndex = gridTiles / 2;
		int canvasW = gridTiles * edge + padding * 2;
		int canvasH = gridTiles * edge + padding * 2;
		var canvas = new byte[canvasW * canvasH * 4];

		int centerTileAnchorX = padding + centerTileIndex * edge;
		int centerTileAnchorY = padding + centerTileIndex * edge;

		// 1. Draw Ground Flooring
		if (SelectedGroundId.HasValue)
		{
			var ground = catalog.TryGetItem(SelectedGroundId.Value);
			if (ground != null && ground.FrameGroups.Count > 0)
			{
				for (int gy = 0; gy < gridTiles; gy++)
				{
					for (int gx = 0; gx < gridTiles; gx++)
					{
						int tileAnchorX = padding + gx * edge;
						int tileAnchorY = padding + gy * edge;
						DrawItemOnCanvas(canvas, canvasW, canvasH, ground, 0, tileAnchorX, tileAnchorY, loader);
					}
				}
			}
		}

		bool isTargetUnderOutfit = IsEffect && targetThing?.IsOnBottom == true;

		// 2. If target is effect with IsOnBottom / below outfit, draw it first
		if (targetThing != null && isTargetUnderOutfit)
		{
			DrawThingWithOffset(canvas, canvasW, canvasH, targetThing, _effectFrame, centerTileAnchorX, centerTileAnchorY, effOffsetX, effOffsetY, loader);
		}

		// 3. Draw Player Outfit (in center tile) if we are in Effect mode OR if the thing is an outfit
		if (IsEffect)
		{
			if (SelectedOutfitId.HasValue)
			{
				var outfit = catalog.TryGetOutfit(SelectedOutfitId.Value);
				if (outfit != null && outfit.FrameGroups.Count > 0)
				{
					DrawOutfitOnCanvas(canvas, canvasW, canvasH, outfit, (int)OutfitDirection, _animationPhase, centerTileAnchorX, centerTileAnchorY, loader);
				}
			}
		}
		else if (IsOutfit && targetThing != null)
		{
			// The thing itself is an outfit! Render it with its custom offset
			DrawOutfitOnCanvas(canvas, canvasW, canvasH, targetThing, (int)OutfitDirection, _animationPhase, centerTileAnchorX, centerTileAnchorY, loader, effOffsetX, effOffsetY);
		}

		// 4. If target is NOT under outfit (or is effect on top, or item, or missile), draw it now
		if (targetThing != null && !isTargetUnderOutfit && !IsOutfit)
		{
			DrawThingWithOffset(canvas, canvasW, canvasH, targetThing, _effectFrame, centerTileAnchorX, centerTileAnchorY, effOffsetX, effOffsetY, loader);
		}

		// 5. Draw center tile boundary dashed guide for reference
		DrawCenterTileGuide(canvas, canvasW, canvasH, centerTileAnchorX, centerTileAnchorY, edge);

		var old = _previewImage;
		PreviewImage = _bitmapRenderer.ConvertRgba(canvasW, canvasH, canvas);
		old?.Dispose();
	}

	private static void DrawCenterTileGuide(byte[] canvas, int canvasW, int canvasH, int tx, int ty, int edge)
	{
		for (int i = 0; i < edge; i += 4)
		{
			SetPixel(canvas, canvasW, canvasH, tx + i, ty, 255, 255, 255, 60);
			SetPixel(canvas, canvasW, canvasH, tx + i + 1, ty, 255, 255, 255, 60);
			SetPixel(canvas, canvasW, canvasH, tx + i, ty + edge - 1, 255, 255, 255, 60);
			SetPixel(canvas, canvasW, canvasH, tx + i + 1, ty + edge - 1, 255, 255, 255, 60);
			SetPixel(canvas, canvasW, canvasH, tx, ty + i, 255, 255, 255, 60);
			SetPixel(canvas, canvasW, canvasH, tx, ty + i + 1, 255, 255, 255, 60);
			SetPixel(canvas, canvasW, canvasH, tx + edge - 1, ty + i, 255, 255, 255, 60);
			SetPixel(canvas, canvasW, canvasH, tx + edge - 1, ty + i + 1, 255, 255, 255, 60);
		}
	}

	private static void DrawItemOnCanvas(byte[] canvas, int canvasW, int canvasH, ThingType item, int frame, int anchorX, int anchorY, SpriteLoader loader)
	{
		if (item.FrameGroups.Count == 0)
			return;

		try
		{
			var selection = ThingFrameResolver.GetItemFrame(item, new ItemFrameRequest { Frame = (uint)frame });
			DrawFrameSelection(canvas, canvasW, canvasH, selection, anchorX, anchorY, 0, 0, loader, baseLayerOnly: false);
		}
		catch { }
	}

	private static void DrawThingWithOffset(byte[] canvas, int canvasW, int canvasH, ThingType thing, int frame, int anchorX, int anchorY, int offsetX, int offsetY, SpriteLoader loader)
	{
		if (thing.FrameGroups.Count == 0)
			return;

		try
		{
			switch (thing.Kind)
			{
				case ThingKind.Effect:
					var effSelection = ThingFrameResolver.GetEffectFrame(thing, new EffectFrameRequest { Frame = (uint)frame });
					DrawFrameSelection(canvas, canvasW, canvasH, effSelection, anchorX, anchorY, offsetX, offsetY, loader, baseLayerOnly: false);
					break;
				case ThingKind.Item:
					var itemSelection = ThingFrameResolver.GetItemFrame(thing, new ItemFrameRequest { Frame = (uint)frame });
					DrawFrameSelection(canvas, canvasW, canvasH, itemSelection, anchorX, anchorY, offsetX, offsetY, loader, baseLayerOnly: false);
					break;
				case ThingKind.Missile:
					var missileSelection = ThingFrameResolver.GetMissileFrame(thing, new MissileFrameRequest { Direction = Direction8.South });
					DrawFrameSelection(canvas, canvasW, canvasH, missileSelection, anchorX, anchorY, offsetX, offsetY, loader, baseLayerOnly: false);
					break;
			}
		}
		catch { }
	}

	private static void DrawOutfitOnCanvas(byte[] canvas, int canvasW, int canvasH, ThingType outfit, int direction, int phase, int anchorX, int anchorY, SpriteLoader loader, int? overrideOffsetX = null, int? overrideOffsetY = null)
	{
		if (outfit.FrameGroups.Count == 0)
			return;

		try
		{
			var selection = ThingFrameResolver.GetOutfitFrame(outfit, new OutfitFrameRequest
			{
				Direction = direction,
				WalkPhase = (uint)phase,
				AddonMask = 0,
			});

			int dispX = overrideOffsetX ?? (outfit.HasOffset ? outfit.OffsetX : 0);
			int dispY = overrideOffsetY ?? (outfit.HasOffset ? outfit.OffsetY : 0);

			if (SettingsViewModel.OffsetPreviewCenterOutfits && !outfit.DontCenterOutfit)
			{
				var fg = selection.FrameGroup;
				const int edge = (int)SpritePixelCodec.SpriteEdgeLength; // 32
				if (fg.Width > 1)
				{
					// For a 2x1 outfit (Width=2, 64px), centering on a 1x1 (32px) tile shifts it right by +16px (half the extra width: (2-1)*32/2 = 16)
					dispX -= (int)((fg.Width - 1) * edge / 2);
				}
			}

			DrawFrameSelection(canvas, canvasW, canvasH, selection, anchorX, anchorY, dispX, dispY, loader, baseLayerOnly: true);
		}
		catch { }
	}

	private static void DrawFrameSelection(byte[] canvas, int canvasW, int canvasH, ThingFrameSelection selection, int anchorX, int anchorY, int dispX, int dispY, SpriteLoader loader, bool baseLayerOnly)
	{
		var fg = selection.FrameGroup;
		const int edge = (int)SpritePixelCodec.SpriteEdgeLength; // 32

		// In game engine: screenX = anchorX - dispX - (w - 1) * edge + (w - 1 - cellX) * edge
		// In other words, inner (south-west) origin is anchorX - dispX - (fg.Width - 1) * edge
		int innerOriginX = anchorX - dispX - (int)((fg.Width - 1) * edge);
		int innerOriginY = anchorY - dispY - (int)((fg.Height - 1) * edge);

		foreach (var slot in selection.EnumerateSpriteSlots().OrderBy(s => s.Layer))
		{
			if (baseLayerOnly && slot.Layer != 0)
				continue;

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

	private static void SetPixel(byte[] canvas, int canvasW, int canvasH, int x, int y, byte r, byte g, byte b, byte a)
	{
		if (x < 0 || x >= canvasW || y < 0 || y >= canvasH) return;
		int idx = (y * canvasW + x) * 4;
		float alpha = a / 255f;
		float invAlpha = 1f - alpha;
		canvas[idx] = (byte)(r * alpha + canvas[idx] * invAlpha);
		canvas[idx + 1] = (byte)(g * alpha + canvas[idx + 1] * invAlpha);
		canvas[idx + 2] = (byte)(b * alpha + canvas[idx + 2] * invAlpha);
		canvas[idx + 3] = (byte)Math.Min(255, canvas[idx + 3] + a);
	}

	private void OnPropertyChanged([CallerMemberName] string? name = null) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
