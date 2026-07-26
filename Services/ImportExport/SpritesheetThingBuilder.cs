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
	int OutfitDirections,
	int OutfitFrames,
	uint OutfitDurationMs,
	bool ImprovedAnimations,
	ThingType? Template,
	ThingType? Replacement);

public sealed record SlicerThingBuildResult(IReadOnlyList<byte[]> SpritePixels, IReadOnlyList<ThingType> Things, bool IsReplacement);

/// <summary>Builds thing definitions while preserving NyxAssets' engine sprite-slot order.</summary>
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

		var byCoordinate = request.Cells.ToDictionary(c => (c.Column, c.Row));
		if (byCoordinate.Count != request.Grid.Columns * request.Grid.Rows)
			throw new InvalidOperationException("The complete grid selection is required for thing import.");

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

		var things = request.Kind == ThingKind.Outfit
			? BuildOutfit(request, spriteIds)
			: BuildFootprintThings(request, spriteIds);

		if (request.Replacement != null && things.Count != 1)
			throw new InvalidOperationException("Replacement requires a selection that produces exactly one thing.");
		return new SlicerThingBuildResult(pixels, things, request.Replacement != null);
	}

	private static IReadOnlyList<ThingType> BuildFootprintThings(SlicerThingBuildRequest request, IReadOnlyDictionary<(int, int), uint> spriteIds)
	{
		if ((request.ThingWidth == 0) != (request.ThingHeight == 0))
			throw new InvalidOperationException("Thing width and height must either both be 0 or both be greater than 0.");

		var width = request.ThingWidth == 0 ? request.Grid.Columns : request.ThingWidth;
		var height = request.ThingHeight == 0 ? request.Grid.Rows : request.ThingHeight;
		if (width <= 0 || height <= 0 || request.Grid.Columns % width != 0 || request.Grid.Rows % height != 0)
			throw new InvalidOperationException($"The {request.Grid.Columns}×{request.Grid.Rows} selection cannot be split into {width}×{height} things.");

		var across = request.Grid.Columns / width;
		var down = request.Grid.Rows / height;
		if (request.Replacement != null && across * down != 1)
			throw new InvalidOperationException("Split selections cannot replace an existing thing.");

		var result = new List<ThingType>(across * down);
		for (var tileRow = 0; tileRow < down; tileRow++)
		for (var tileColumn = 0; tileColumn < across; tileColumn++)
		{
			var id = request.Replacement?.Id ?? request.FirstThingId + (uint)result.Count;
			ThingType thing;
			if (request.Replacement != null)
				thing = ThingCloner.Clone(request.Replacement, id);
			else if (request.Template != null)
			{
				thing = ThingCloner.Clone(request.Template, id);
			}
			else
				thing = new ThingType { Id = id, Kind = request.Kind };

			thing.Id = id;
			thing.Kind = request.Kind;
			thing.FrameGroups.Clear();
			var group = CreateGroup((uint)width, (uint)height, 1, 1);
			for (var screenY = 0; screenY < height; screenY++)
			for (var screenX = 0; screenX < width; screenX++)
			{
				// Thing sprite coordinates are stored bottom-up and right-to-left, not screen row-major.
				var innerW = (uint)(width - 1 - screenX);
				var innerH = (uint)(height - 1 - screenY);
				var index = group.GetSpriteIndex(innerW, innerH, 0, 0, 0, 0, 0);
				group.SpriteIds[index] = spriteIds[(tileColumn * width + screenX, tileRow * height + screenY)];
			}
			thing.FrameGroups.Add(group);
			result.Add(thing);
		}
		return result;
	}

	private static IReadOnlyList<ThingType> BuildOutfit(SlicerThingBuildRequest request, IReadOnlyDictionary<(int, int), uint> spriteIds)
	{
		var directions = request.OutfitDirections;
		var frames = request.OutfitFrames;
		if (directions <= 0 || frames <= 0 || request.Grid.Columns % directions != 0 || request.Grid.Rows % frames != 0)
			throw new InvalidOperationException($"Outfit selection must be divisible by {directions} directions and {frames} frames.");

		var width = request.Grid.Columns / directions;
		var height = request.Grid.Rows / frames;
		var id = request.Replacement?.Id ?? request.FirstThingId;
		var thing = request.Replacement != null
			? ThingCloner.Clone(request.Replacement, id)
			: request.Template != null
				? ThingCloner.Clone(request.Template, id)
				: new ThingType { Id = id, Kind = ThingKind.Outfit };
		thing.Id = id;
		thing.Kind = ThingKind.Outfit;
		thing.FrameGroups.Clear();

		var group = CreateGroup((uint)width, (uint)height, (uint)directions, (uint)frames);
		group.IsAnimation = frames > 1;
		if (request.ImprovedAnimations && frames > 1)
		{
			group.FrameTimings = Enumerable.Range(0, frames)
				.Select(_ => new AnimationFrameTiming(request.OutfitDurationMs, request.OutfitDurationMs))
				.ToArray();
		}

		for (var frame = 0; frame < frames; frame++)
		for (var direction = 0; direction < directions; direction++)
		for (var screenY = 0; screenY < height; screenY++)
		for (var screenX = 0; screenX < width; screenX++)
		{
			// The sheet is direction-major horizontally and frame-major vertically. Inner tile
			// coordinates must still be reversed to match the engine's frame-group packing order.
			var innerW = (uint)(width - 1 - screenX);
			var innerH = (uint)(height - 1 - screenY);
			var index = group.GetSpriteIndex(innerW, innerH, 0, (uint)direction, 0, 0, (uint)frame);
			group.SpriteIds[index] = spriteIds[(direction * width + screenX, frame * height + screenY)];
		}

		thing.FrameGroups.Add(group);
		return new[] { thing };
	}

	private static ThingFrameGroup CreateGroup(uint width, uint height, uint patternX, uint frames)
	{
		var group = new ThingFrameGroup
		{
			GroupTypeId = 0,
			Width = width,
			Height = height,
			ExactSize = 32,
			Layers = 1,
			PatternX = patternX,
			PatternY = 1,
			PatternZ = 1,
			Frames = frames,
			SpriteIds = Array.Empty<uint>()
		};
		ThingFrameGroupEditor.EnsureSpriteCapacity(group);
		return group;
	}
}
