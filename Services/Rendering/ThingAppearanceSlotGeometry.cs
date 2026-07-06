using System;
using NyxAssets.Sprites;
using NyxAssets.Things;
using NyxAssets.Things.Frames;
using NyxAssets.Utils;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;

namespace NyxAssetsEditor.Services.Rendering;

public static class ThingAppearanceSlotGeometry
{
	public static (int X, int Y, int Width, int Height)? GetHighlightRect(
		FloatingThingEditorViewModel vm,
		ThingAppearanceSlot slot)
	{
		var fg = vm.CurrentFrameGroup;
		if (fg.Width == 0 || fg.Height == 0)
			return null;

		var edge = SpritePixelCodec.SpriteEdgeLength;
		var cellW = (int)(fg.Width * edge);
		var cellH = (int)(fg.Height * edge);

		int cellOffsetX;
		int cellOffsetY;

		if (vm.IsMissile)
		{
			var compass = GetMissileCompassCell(slot.PatternX, slot.PatternY);
			if (compass == null)
				return null;

			cellOffsetX = compass.Value.Column * cellW;
			cellOffsetY = compass.Value.Row * cellH;
		}
		else if (vm.ShowPatternGrid)
		{
			cellOffsetX = (int)(slot.PatternX * cellW);
			cellOffsetY = (int)(slot.PatternY * cellH);
		}
		else
		{
			cellOffsetX = 0;
			cellOffsetY = 0;
		}

		var tileX = cellOffsetX + (int)((fg.Width - slot.InnerW - 1) * edge);
		var tileY = cellOffsetY + (int)((fg.Height - slot.InnerH - 1) * edge);
		return (tileX, tileY, edge, edge);
	}

	private static (int Column, int Row)? GetMissileCompassCell(uint patternX, uint patternY)
	{
		ReadOnlySpan<(Direction8 Direction, int Column, int Row)> slots =
		[
			(Direction8.NorthWest, 0, 0),
			(Direction8.North, 1, 0),
			(Direction8.NorthEast, 2, 0),
			(Direction8.West, 0, 1),
			(Direction8.East, 2, 1),
			(Direction8.SouthWest, 0, 2),
			(Direction8.South, 1, 2),
			(Direction8.SouthEast, 2, 2),
		];

		foreach (var (direction, column, row) in slots)
		{
			var (px, py) = MissileDirectionPatterns.GetPattern(direction);
			if (px == patternX && py == patternY)
				return (column, row);
		}

		return null;
	}
}
