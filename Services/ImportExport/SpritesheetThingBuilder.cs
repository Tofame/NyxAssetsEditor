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
	bool OutfitFrameGroups = false,
	int ExactSize = 32,
	int OutfitIdleFrames = 0);

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
		if (request.ExactSize <= 0)
			throw new InvalidOperationException("Exact size must be greater than 0.");
		if (request.OutfitIdleFrames < 0 || request.OutfitIdleFrames >= request.Frames)
			throw new InvalidOperationException("Idle frames must leave at least one walking frame.");

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
		var layoutGroups = ResolveLayoutGroups(request, layoutSource);
		var things = BuildThingsFromSpriteSheet(request, spriteIds, layoutGroups);
		if (request.Kind == ThingKind.Outfit)
		{
			foreach (var thing in things)
			{
				if (request.OutfitFrameGroups) SplitOutfitFrames(thing, request);
				else CollapseOutfitFrameGroups(thing, request);
			}
		}
		if (request.Replacement != null && things.Count != 1)
			throw new InvalidOperationException("Replacement requires a selection that produces exactly one thing.");
		return new SlicerThingBuildResult(pixels, things, request.Replacement != null);
	}

	private static IReadOnlyList<ThingFrameGroup> ResolveLayoutGroups(
		SlicerThingBuildRequest request,
		ThingType? layoutSource)
	{
		if (layoutSource is { FrameGroups.Count: > 1 })
			return layoutSource.FrameGroups;

		var textureColumns = checked(request.Layers * request.PatternX * request.PatternZ);
		var textureRows = checked(request.Frames * request.PatternY);
		int width;
		int height;
		if (request.ThingWidth == 0)
		{
			if (request.Grid.Columns % textureColumns != 0 || request.Grid.Rows % textureRows != 0)
				throw new InvalidOperationException(
					$"The {request.Grid.Columns}×{request.Grid.Rows} selection does not contain one complete {textureColumns}×{textureRows} thing layout.");
			width = request.Grid.Columns / textureColumns;
			height = request.Grid.Rows / textureRows;
		}
		else
		{
			width = request.ThingWidth;
			height = request.ThingHeight;
		}

		if (width <= 0 || height <= 0)
			throw new InvalidOperationException("Thing width and height must be greater than 0 after layout inference.");

		if (request.Kind == ThingKind.Outfit && request.OutfitIdleFrames > 0)
		{
			var walkingFrames = checked(request.Frames - request.OutfitIdleFrames);
			return new[]
			{
				CreateGroup(request, (uint)width, (uint)height, groupType: 0, frames: request.OutfitIdleFrames),
				CreateGroup(request, (uint)width, (uint)height, groupType: 1, frames: walkingFrames)
			};
		}

		return new[] { CreateGroup(request, (uint)width, (uint)height, groupType: 0, frames: request.Frames) };
	}

	private static IReadOnlyList<ThingType> BuildThingsFromSpriteSheet(
		SlicerThingBuildRequest request,
		IReadOnlyDictionary<(int, int), uint> spriteIds,
		IReadOnlyList<ThingFrameGroup> layoutGroups)
	{
		if (layoutGroups.Count == 0 || layoutGroups.Any(group => group.Width == 0 || group.Height == 0 ||
			group.Layers == 0 || group.PatternX == 0 || group.PatternY == 0 || group.PatternZ == 0 || group.Frames == 0))
			throw new InvalidOperationException("The thing contains an invalid frame-group layout.");

		// This is the exact inverse of ThingSpriteSheetExporter.CompositeFrameGroupOnto:
		// every group uses the widest texture row and largest footprint, groups are
		// stacked vertically, and GetTextureIndex/GetSpriteIndex define slot order.
		var totalX = checked((int)layoutGroups.Max(group => group.GetSpriteSheetTextureColumns()));
		var maxTileWidth = checked((int)layoutGroups.Max(group => group.Width));
		var maxTileHeight = checked((int)layoutGroups.Max(group => group.Height));
		var textureRows = layoutGroups.Select(group => checked((int)group.GetSpriteSheetTextureRows())).ToArray();
		var sheetColumns = checked(totalX * maxTileWidth);
		var sheetRows = checked(textureRows.Sum() * maxTileHeight);
		if (sheetColumns <= 0 || sheetRows <= 0 || request.Grid.Columns % sheetColumns != 0 || request.Grid.Rows % sheetRows != 0)
			throw new InvalidOperationException(
				$"The {request.Grid.Columns}×{request.Grid.Rows} selection cannot be split into {sheetColumns}×{sheetRows} thing sheets.");

		var across = request.Grid.Columns / sheetColumns;
		var down = request.Grid.Rows / sheetRows;
		if (request.Replacement != null && across * down != 1)
			throw new InvalidOperationException("Split selections cannot replace an existing thing.");

		var result = new List<ThingType>(checked(across * down));
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

			var groupRowOffset = 0;
			for (var groupIndex = 0; groupIndex < layoutGroups.Count; groupIndex++)
			{
				var group = CreateGroupFromLayout(layoutGroups[groupIndex]);
				for (var frame = 0u; frame < group.Frames; frame++)
				for (var patternZ = 0u; patternZ < group.PatternZ; patternZ++)
				for (var patternY = 0u; patternY < group.PatternY; patternY++)
				for (var patternX = 0u; patternX < group.PatternX; patternX++)
				for (var layer = 0u; layer < group.Layers; layer++)
				for (var innerW = 0u; innerW < group.Width; innerW++)
				for (var innerH = 0u; innerH < group.Height; innerH++)
				{
					var textureIndex = group.GetTextureIndex(layer, patternX, patternY, patternZ, frame);
					var textureColumn = checked((int)(textureIndex % (uint)totalX));
					var textureRow = checked((int)(textureIndex / (uint)totalX));
					var screenX = checked((int)(group.Width - innerW - 1));
					var screenY = checked((int)(group.Height - innerH - 1));
					var sourceColumn = tileColumn * sheetColumns + textureColumn * maxTileWidth + screenX;
					var sourceRow = tileRow * sheetRows + groupRowOffset + textureRow * maxTileHeight + screenY;
					var index = group.GetSpriteIndex(innerW, innerH, layer, patternX, patternY, patternZ, frame);
					group.SpriteIds[index] = spriteIds[(sourceColumn, sourceRow)];
				}

				thing.FrameGroups.Add(group);
				groupRowOffset += checked(textureRows[groupIndex] * maxTileHeight);
			}

			result.Add(thing);
		}

		return result;
	}

	private static void SplitOutfitFrames(ThingType thing, SlicerThingBuildRequest request)
	{
		if (thing.FrameGroups.Count != 1) return;
		var normal = thing.FrameGroups[0];
		if (normal.Frames < 2 || (request.OutfitIdleFrames == 0 && normal.Frames < 3)) return;
		var idleFrames = request.OutfitIdleFrames > 0 ? request.OutfitIdleFrames : 1;
		if (idleFrames >= normal.Frames)
			throw new InvalidOperationException("Idle frames must leave at least one walking frame.");
		var slotsPerFrame = normal.SpriteIds.Length / checked((int)normal.Frames);
		var idleSpriteCount = checked(slotsPerFrame * idleFrames);
		var idleIds = normal.SpriteIds.Take(idleSpriteCount).ToArray();
		var walkingIds = normal.SpriteIds.Skip(idleSpriteCount).ToArray();
		var idle = CreateDerivedOutfitGroup(normal, 0, idleFrames, idleIds, request);
		var walking = CreateDerivedOutfitGroup(normal, 1, checked((int)normal.Frames - idleFrames), walkingIds, request);
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

	private static ThingFrameGroup CreateGroup(
		SlicerThingBuildRequest request,
		uint width,
		uint height,
		byte groupType,
		int frames)
	{
		var group = new ThingFrameGroup
		{
			GroupTypeId = groupType,
			Width = width,
			Height = height,
			ExactSize = (uint)request.ExactSize,
			Layers = (uint)request.Layers,
			PatternX = (uint)request.PatternX,
			PatternY = (uint)request.PatternY,
			PatternZ = (uint)request.PatternZ,
			Frames = (uint)frames,
			SpriteIds = Array.Empty<uint>(),
			IsAnimation = frames > 1
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
