using System;
using System.Collections.Generic;
using System.Linq;
using NyxAssets.Things;
using NyxAssetsEditor.Services.Exchange;

namespace NyxAssetsEditor.Services.ImportExport;

public sealed record SlicerThingBuildRequest(
	ThingKind Kind,
	SlicerGrid Grid,
	IReadOnlyList<SlicerCell> Cells,
	uint FirstSpriteId,
	uint FirstThingId,
	int ThingWidth,
	int ThingHeight,
	int Layers,
	int PatternX,
	int PatternY,
	int PatternZ,
	int Frames,
	uint AnimationDurationMs,
	bool ImprovedAnimations,
	ThingType? Template,
	ThingType? Replacement,
	bool OutfitFrameGroups = false);

public sealed record SlicerThingBuildResult(IReadOnlyList<byte[]> SpritePixels, IReadOnlyList<ThingType> Things, bool IsReplacement);

/// <summary>Builds thing definitions while preserving Object Builder and NyxAssets frame-group order.</summary>
public static class SpritesheetThingBuilder
{
	public static SlicerThingBuildResult Build(SlicerThingBuildRequest request)
	{
		if (request.Grid.Columns <= 0 || request.Grid.Rows <= 0)
			throw new InvalidOperationException("Select at least one complete sprite cell.");
		if (request.Replacement != null && request.Replacement.Kind != request.Kind)
			throw new InvalidOperationException("The replacement target must have the selected thing kind.");
		if (request.Template != null && request.Template.Kind != request.Kind)
			throw new InvalidOperationException("The template must have the selected thing kind.");
		if ((request.ThingWidth == 0) != (request.ThingHeight == 0))
			throw new InvalidOperationException("Thing width and height must either both be 0 or both be greater than 0.");
		if (request.Layers <= 0 || request.PatternX <= 0 || request.PatternY <= 0 || request.PatternZ <= 0 || request.Frames <= 0)
			throw new InvalidOperationException("Layers, patterns, directions, and frames must all be greater than 0.");

		var byCoordinate = request.Cells.ToDictionary(c => (c.Column, c.Row));
		if (byCoordinate.Count != request.Grid.Columns * request.Grid.Rows)
			throw new InvalidOperationException("The complete grid selection is required for thing import.");

		// Empty cells are structural frame-group slots. They reference sprite ID 0;
		// only non-empty pixel buffers are appended to the SPR archive.
		var pixels = new List<byte[]>();
		var spriteIds = new Dictionary<(int Column, int Row), uint>();
		var nextSpriteId = request.FirstSpriteId;
		for (var row = 0; row < request.Grid.Rows; row++)
		for (var column = 0; column < request.Grid.Columns; column++)
		{
			var cell = byCoordinate[(column, row)];
			if (cell.IsEmpty)
			{
				spriteIds[(column, row)] = 0;
				continue;
			}
			spriteIds[(column, row)] = nextSpriteId++;
			pixels.Add(cell.Rgba);
		}

		var layoutSource = request.Replacement ?? request.Template;
		var things = layoutSource is { FrameGroups.Count: > 1 }
			? BuildCombinedFrameGroups(request, spriteIds, layoutSource)
			: BuildThings(request, spriteIds);
		if (request.Kind == ThingKind.Outfit)
		{
			foreach (var thing in things)
			{
				if (request.OutfitFrameGroups) SplitLegacyOutfitFrames(thing, request);
				else CollapseOutfitFrameGroups(thing, request);
			}
		}
		if (request.Replacement != null && things.Count != 1)
			throw new InvalidOperationException("Replacement requires a selection that produces exactly one thing.");
		return new SlicerThingBuildResult(pixels, things, request.Replacement != null);
	}

	private static IReadOnlyList<ThingType> BuildThings(
		SlicerThingBuildRequest request,
		IReadOnlyDictionary<(int, int), uint> spriteIds)
	{
		var textureColumns = checked(request.Layers * request.PatternX * request.PatternZ);
		var textureRows = checked(request.Frames * request.PatternY);
		int width;
		int height;
		if (request.ThingWidth == 0)
		{
			if (request.Grid.Columns % textureColumns != 0 || request.Grid.Rows % textureRows != 0)
				throw new InvalidOperationException(
					$"The {request.Grid.Columns}×{request.Grid.Rows} selection does not contain one complete {textureColumns}×{textureRows} frame-group layout.");
			width = request.Grid.Columns / textureColumns;
			height = request.Grid.Rows / textureRows;
		}
		else
		{
			width = request.ThingWidth;
			height = request.ThingHeight;
		}

		var sheetColumns = checked(width * textureColumns);
		var sheetRows = checked(height * textureRows);
		if (width <= 0 || height <= 0 || request.Grid.Columns % sheetColumns != 0 || request.Grid.Rows % sheetRows != 0)
			throw new InvalidOperationException(
				$"The {request.Grid.Columns}×{request.Grid.Rows} selection cannot be split into {sheetColumns}×{sheetRows} frame-group sheets.");
		if ((long)sheetColumns * sheetRows > int.MaxValue)
			throw new InvalidOperationException("The frame-group layout contains too many sprite slots.");

		var across = request.Grid.Columns / sheetColumns;
		var down = request.Grid.Rows / sheetRows;
		if (request.Replacement != null && across * down != 1)
			throw new InvalidOperationException("Split selections cannot replace an existing thing.");

		var result = new List<ThingType>(across * down);
		for (var tileRow = 0; tileRow < down; tileRow++)
		for (var tileColumn = 0; tileColumn < across; tileColumn++)
		{
			var id = request.Replacement?.Id ?? request.FirstThingId + (uint)result.Count;
			var thing = request.Replacement != null
				? ThingCloner.Clone(request.Replacement, id)
				: request.Template != null
					? ThingCloner.Clone(request.Template, id)
					: new ThingType { Id = id, Kind = request.Kind };

			thing.Id = id;
			thing.Kind = request.Kind;
			thing.FrameGroups.Clear();
			var group = CreateGroup(request, (uint)width, (uint)height);

			for (var frame = 0; frame < request.Frames; frame++)
			for (var patternZ = 0; patternZ < request.PatternZ; patternZ++)
			for (var patternY = 0; patternY < request.PatternY; patternY++)
			for (var patternX = 0; patternX < request.PatternX; patternX++)
			for (var layer = 0; layer < request.Layers; layer++)
			for (var screenY = 0; screenY < height; screenY++)
			for (var screenX = 0; screenX < width; screenX++)
			{
				// Object Builder packs textures in frame -> Z -> Y -> X -> layer order.
				// Its sheet uses textureIndex % totalX for the texture column and
				// textureIndex / totalX for the row. Keep this formula beside the
				// GetSpriteIndex call to prevent row-major packing regressions.
				var textureIndex = (((((frame * request.PatternZ + patternZ) * request.PatternY + patternY)
					* request.PatternX + patternX) * request.Layers) + layer);
				var textureColumn = textureIndex % textureColumns;
				var textureRow = textureIndex / textureColumns;
				var innerW = (uint)(width - 1 - screenX);
				var innerH = (uint)(height - 1 - screenY);
				var index = group.GetSpriteIndex(innerW, innerH, (uint)layer, (uint)patternX,
					(uint)patternY, (uint)patternZ, (uint)frame);
				var sourceColumn = tileColumn * sheetColumns + textureColumn * width + screenX;
				var sourceRow = tileRow * sheetRows + textureRow * height + screenY;
				group.SpriteIds[index] = spriteIds[(sourceColumn, sourceRow)];
			}

			thing.FrameGroups.Add(group);
			result.Add(thing);
		}
		return result;
	}

	private static IReadOnlyList<ThingType> BuildCombinedFrameGroups(
		SlicerThingBuildRequest request,
		IReadOnlyDictionary<(int, int), uint> spriteIds,
		ThingType layoutSource)
	{
		var sourceGroups = layoutSource.FrameGroups.OrderBy(group => group.GroupTypeId).ToList();
		if (sourceGroups.Any(group => group.Width == 0 || group.Height == 0 || group.Layers == 0 ||
			group.PatternX == 0 || group.PatternY == 0 || group.PatternZ == 0 || group.Frames == 0))
			throw new InvalidOperationException("The selected source thing contains an invalid frame-group layout.");

		var commonWidth = sourceGroups.Max(group => checked((int)group.Width));
		var commonHeight = sourceGroups.Max(group => checked((int)group.Height));
		var totalX = sourceGroups.Max(group => checked((int)(group.PatternZ * group.PatternX * group.Layers)));
		var groupTextureRows = sourceGroups
			.Select(group => checked((int)(group.Frames * group.PatternY)))
			.ToArray();
		var expectedColumns = checked(totalX * commonWidth);
		var expectedRows = checked(groupTextureRows.Sum() * commonHeight);
		if (request.Grid.Columns != expectedColumns || request.Grid.Rows != expectedRows)
			throw new InvalidOperationException(
				$"This combined Object Builder sheet must be exactly {expectedColumns}×{expectedRows} cells for the selected source thing.");

		var id = request.Replacement?.Id ?? request.FirstThingId;
		var thing = request.Replacement != null
			? ThingCloner.Clone(request.Replacement, id)
			: ThingCloner.Clone(request.Template!, id);
		thing.Id = id;
		thing.Kind = request.Kind;
		thing.FrameGroups.Clear();

		var groupRowOffset = 0;
		for (var groupIndex = 0; groupIndex < sourceGroups.Count; groupIndex++)
		{
			var source = sourceGroups[groupIndex];
			var group = CreateGroupFromLayout(source);
			for (var frame = 0; frame < (int)source.Frames; frame++)
			for (var patternZ = 0; patternZ < (int)source.PatternZ; patternZ++)
			for (var patternY = 0; patternY < (int)source.PatternY; patternY++)
			for (var patternX = 0; patternX < (int)source.PatternX; patternX++)
			for (var layer = 0; layer < (int)source.Layers; layer++)
			for (var screenY = 0; screenY < (int)source.Height; screenY++)
			for (var screenX = 0; screenX < (int)source.Width; screenX++)
			{
				// Object Builder's total sheet uses the largest texture-row width and
				// largest footprint across both groups, then stacks each group vertically.
				var textureIndex = (((((frame * (int)source.PatternZ + patternZ) * (int)source.PatternY + patternY)
					* (int)source.PatternX + patternX) * (int)source.Layers) + layer);
				var textureColumn = textureIndex % totalX;
				var textureRow = textureIndex / totalX;
				var innerW = source.Width - 1u - (uint)screenX;
				var innerH = source.Height - 1u - (uint)screenY;
				var index = group.GetSpriteIndex(innerW, innerH, (uint)layer, (uint)patternX,
					(uint)patternY, (uint)patternZ, (uint)frame);
				var sourceColumn = textureColumn * commonWidth + screenX;
				var sourceRow = groupRowOffset + textureRow * commonHeight + screenY;
				group.SpriteIds[index] = spriteIds[(sourceColumn, sourceRow)];
			}
			thing.FrameGroups.Add(group);
			groupRowOffset += groupTextureRows[groupIndex] * commonHeight;
		}

		return new[] { thing };
	}

	private static void SplitLegacyOutfitFrames(ThingType thing, SlicerThingBuildRequest request)
	{
		if (thing.FrameGroups.Count != 1) return;
		var normal = thing.FrameGroups[0];
		if (normal.Frames < 3) return;
		var slotsPerFrame = normal.SpriteIds.Length / checked((int)normal.Frames);
		var idleIds = normal.SpriteIds.Take(slotsPerFrame).ToArray();
		var walkingIds = normal.SpriteIds.Skip(slotsPerFrame).ToArray();
		var idle = CreateDerivedOutfitGroup(normal, 0, 1, idleIds, request);
		var walking = CreateDerivedOutfitGroup(normal, 1, checked((int)normal.Frames - 1), walkingIds, request);
		thing.FrameGroups.Clear();
		thing.FrameGroups.Add(idle);
		thing.FrameGroups.Add(walking);
	}

	private static void CollapseOutfitFrameGroups(ThingType thing, SlicerThingBuildRequest request)
	{
		if (thing.FrameGroups.Count <= 1) return;
		var groups = thing.FrameGroups.OrderBy(group => group.GroupTypeId).ToList();
		var first = groups[0];
		if (groups.Any(group => group.Width != first.Width || group.Height != first.Height ||
			group.Layers != first.Layers || group.PatternX != first.PatternX ||
			group.PatternY != first.PatternY || group.PatternZ != first.PatternZ))
			throw new InvalidOperationException("The selected frame groups cannot be represented by this legacy target because their layouts differ.");

		var frames = checked(groups.Sum(group => (int)group.Frames));
		var spriteIds = groups.SelectMany(group => group.SpriteIds).ToArray();
		var normal = CreateDerivedOutfitGroup(first, 0, frames, spriteIds, request);
		thing.FrameGroups.Clear();
		thing.FrameGroups.Add(normal);
	}

	private static ThingFrameGroup CreateDerivedOutfitGroup(
		ThingFrameGroup source,
		int groupType,
		int frames,
		uint[] spriteIds,
		SlicerThingBuildRequest request)
	{
		var group = new ThingFrameGroup
		{
			GroupTypeId = (byte)groupType,
			Width = source.Width,
			Height = source.Height,
			ExactSize = source.ExactSize,
			Layers = source.Layers,
			PatternX = source.PatternX,
			PatternY = source.PatternY,
			PatternZ = source.PatternZ,
			Frames = (uint)frames,
			IsAnimation = frames > 1,
			AnimationMode = 0,
			LoopCount = 0,
			StartFrame = 0,
			SpriteIds = spriteIds
		};
		if (request.ImprovedAnimations && frames > 1)
		{
			group.FrameTimings = Enumerable.Range(0, frames)
				.Select(_ => new AnimationFrameTiming(request.AnimationDurationMs, request.AnimationDurationMs))
				.ToArray();
		}
		ThingFrameGroupEditor.EnsureSpriteCapacity(group);
		return group;
	}

	private static ThingFrameGroup CreateGroup(SlicerThingBuildRequest request, uint width, uint height)
	{
		var group = new ThingFrameGroup
		{
			GroupTypeId = 0,
			Width = width,
			Height = height,
			ExactSize = 32,
			Layers = (uint)request.Layers,
			PatternX = (uint)request.PatternX,
			PatternY = (uint)request.PatternY,
			PatternZ = (uint)request.PatternZ,
			Frames = (uint)request.Frames,
			SpriteIds = Array.Empty<uint>(),
			IsAnimation = request.Frames > 1
		};
		if (request.ImprovedAnimations && request.Frames > 1)
		{
			group.FrameTimings = Enumerable.Range(0, request.Frames)
				.Select(_ => new AnimationFrameTiming(request.AnimationDurationMs, request.AnimationDurationMs))
				.ToArray();
		}
		ThingFrameGroupEditor.EnsureSpriteCapacity(group);
		return group;
	}

	private static ThingFrameGroup CreateGroupFromLayout(ThingFrameGroup source)
	{
		var group = new ThingFrameGroup
		{
			GroupTypeId = source.GroupTypeId,
			Width = source.Width,
			Height = source.Height,
			ExactSize = source.ExactSize,
			Layers = source.Layers,
			PatternX = source.PatternX,
			PatternY = source.PatternY,
			PatternZ = source.PatternZ,
			Frames = source.Frames,
			IsAnimation = source.IsAnimation,
			AnimationMode = source.AnimationMode,
			LoopCount = source.LoopCount,
			StartFrame = source.StartFrame,
			SpriteIds = Array.Empty<uint>()
		};
		if (source.FrameTimings != null)
		{
			group.FrameTimings = source.FrameTimings
				.Select(timing => new AnimationFrameTiming(timing.MinimumMilliseconds, timing.MaximumMilliseconds))
				.ToArray();
		}
		ThingFrameGroupEditor.EnsureSpriteCapacity(group);
		return group;
	}
}
