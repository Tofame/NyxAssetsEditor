using System;
using NyxAssets.Sprites;
using NyxAssets.Things;
using NyxAssets.Things.Frames;
using NyxAssets.Utils;
using NyxAssetsEditor.ViewModels.ArchiveLoaders;

namespace NyxAssetsEditor.Services.Rendering;

public readonly record struct ThingAppearanceSlot(uint InnerW, uint InnerH, uint PatternX, uint PatternY);

public static class ThingAppearanceDropTarget
{
	public static ThingAppearanceSlot? Resolve(
		FloatingThingEditorViewModel vm,
		double dropX,
		double dropY,
		int imageWidth,
		int imageHeight)
	{
		if (imageWidth <= 0 || imageHeight <= 0)
			return null;

		var fg = vm.CurrentFrameGroup;
		var edge = SpritePixelCodec.SpriteEdgeLength;

		if (vm.IsMissile)
			return ResolveMissile(fg, edge, dropX, dropY, imageWidth, imageHeight);

		if (vm.ShowPatternGrid)
			return ResolvePatternGrid(fg, edge, dropX, dropY, imageWidth, imageHeight);

		return ResolveSingleTile(fg, edge, dropX, dropY, imageWidth, imageHeight, vm.ViewPatternXIndex, vm.ViewPatternYIndex);
	}

	private static ThingAppearanceSlot? ResolveMissile(
		ThingFrameGroup fg,
		int edge,
		double dropX,
		double dropY,
		int imageWidth,
		int imageHeight)
	{
		var cellW = imageWidth / 3.0;
		var cellH = imageHeight / 3.0;
		if (cellW <= 0 || cellH <= 0)
			return null;

		var col = (int)Math.Clamp(dropX / cellW, 0, 2);
		var row = (int)Math.Clamp(dropY / cellH, 0, 2);
		if (col == 1 && row == 1)
			return null;

		var direction = (col, row) switch
		{
			(0, 0) => Direction8.NorthWest,
			(1, 0) => Direction8.North,
			(2, 0) => Direction8.NorthEast,
			(0, 1) => Direction8.West,
			(2, 1) => Direction8.East,
			(0, 2) => Direction8.SouthWest,
			(1, 2) => Direction8.South,
			(2, 2) => Direction8.SouthEast,
			_ => Direction8.South,
		};

		var (patternX, patternY) = MissileDirectionPatterns.GetPattern(direction);
		var localX = dropX - col * cellW;
		var localY = dropY - row * cellH;
		return ResolveSingleTile(fg, edge, localX, localY, (int)cellW, (int)cellH, (int)patternX, (int)patternY);
	}

	private static ThingAppearanceSlot? ResolvePatternGrid(
		ThingFrameGroup fg,
		int edge,
		double dropX,
		double dropY,
		int imageWidth,
		int imageHeight)
	{
		var cellW = fg.Width * edge;
		var cellH = fg.Height * edge;
		if (cellW <= 0 || cellH <= 0)
			return null;

		var patternX = (uint)Math.Clamp((int)(dropX / cellW), 0, Math.Max(0, (int)fg.PatternX - 1));
		var patternY = (uint)Math.Clamp((int)(dropY / cellH), 0, Math.Max(0, (int)fg.PatternY - 1));
		var localX = dropX - patternX * cellW;
		var localY = dropY - patternY * cellH;
		return ResolveSingleTile(fg, edge, localX, localY, (int)cellW, (int)cellH, (int)patternX, (int)patternY);
	}

	private static ThingAppearanceSlot? ResolveSingleTile(
		ThingFrameGroup fg,
		int edge,
		double dropX,
		double dropY,
		int cellWidth,
		int cellHeight,
		int patternX,
		int patternY)
	{
		if (dropX < 0 || dropY < 0 || dropX >= cellWidth || dropY >= cellHeight)
			return null;

		if (fg.Width == 0 || fg.Height == 0)
			return null;

		var innerW = (uint)Math.Clamp(fg.Width - 1 - (int)(dropX / edge), 0, (int)fg.Width - 1);
		var innerH = (uint)Math.Clamp(fg.Height - 1 - (int)(dropY / edge), 0, (int)fg.Height - 1);
		return new ThingAppearanceSlot(innerW, innerH, (uint)patternX, (uint)patternY);
	}
}
