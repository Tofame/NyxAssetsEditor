using System.Collections.Generic;
using NyxAssets.Things;
using NyxAssets.Things.Frames;
using NyxAssetsEditor.Services.Exchange;
using Xunit;

namespace NyxAssetsEditor.Tests;

public sealed class ThingFrameGroupEditorTests
{
	[Fact]
	public void IncreaseFrames_PreservesExistingSpriteIdsByCoordinate()
	{
		// PatternX=4 so flat Array.Copy would scramble after Frames change
		var group = OutfitWalkGroup(frames: 2, patternX: 4);
		FillDistinctIds(group);

		var before = SnapshotIds(group);
		var snap = ThingFrameGroupEditor.CaptureSpriteLayout(group);

		group.Frames = 4;
		ThingFrameGroupEditor.RemapSpriteIdsAfterDimensionChange(group, snap);

		Assert.Equal(group.GetTotalSpriteSlots(), (uint)group.SpriteIds.Length);

		for (uint f = 0; f < 2; f++)
		for (uint px = 0; px < 4; px++)
			Assert.Equal(before[(f, px)], group.GetSpriteId(0, 0, 0, px, 0, 0, f));

		for (uint f = 2; f < 4; f++)
		for (uint px = 0; px < 4; px++)
			Assert.Equal(0u, group.GetSpriteId(0, 0, 0, px, 0, 0, f));
	}

	[Fact]
	public void DecreaseFrames_KeepsLeadingFramesAndDropsTrailing()
	{
		var group = OutfitWalkGroup(frames: 4, patternX: 4);
		FillDistinctIds(group);

		var before = SnapshotIds(group);
		var snap = ThingFrameGroupEditor.CaptureSpriteLayout(group);

		group.Frames = 2;
		ThingFrameGroupEditor.RemapSpriteIdsAfterDimensionChange(group, snap);

		Assert.Equal(group.GetTotalSpriteSlots(), (uint)group.SpriteIds.Length);

		for (uint f = 0; f < 2; f++)
		for (uint px = 0; px < 4; px++)
			Assert.Equal(before[(f, px)], group.GetSpriteId(0, 0, 0, px, 0, 0, f));

		// Trailing frame ids must not leak into the shortened array at wrong slots
		Assert.DoesNotContain(before[(3, 0)], group.SpriteIds);
		Assert.DoesNotContain(before[(2, 3)], group.SpriteIds);
	}

	[Fact]
	public void IncreaseThenDecreaseFrames_RoundTripsLeadingSpriteIds()
	{
		var group = OutfitWalkGroup(frames: 3, patternX: 4);
		FillDistinctIds(group);
		var original = SnapshotIds(group);

		var snapUp = ThingFrameGroupEditor.CaptureSpriteLayout(group);
		group.Frames = 5;
		ThingFrameGroupEditor.RemapSpriteIdsAfterDimensionChange(group, snapUp);

		var snapDown = ThingFrameGroupEditor.CaptureSpriteLayout(group);
		group.Frames = 3;
		ThingFrameGroupEditor.RemapSpriteIdsAfterDimensionChange(group, snapDown);

		for (uint f = 0; f < 3; f++)
		for (uint px = 0; px < 4; px++)
			Assert.Equal(original[(f, px)], group.GetSpriteId(0, 0, 0, px, 0, 0, f));
	}

	[Fact]
	public void RemapDoesNotFlatCopy_RegressionAgainstScrambledLayout()
	{
		// With Frames outermost, bumping Frames from 1→2 doubles capacity.
		// Flat prefix copy would put old dir1 id where new frame0/dir1 should be OK
		// for frame 0 only by luck — verify dir slots stay correct after grow.
		var group = OutfitWalkGroup(frames: 1, patternX: 4);
		group.SpriteIds[group.GetSpriteIndex(0, 0, 0, 0, 0, 0, 0)] = 100;
		group.SpriteIds[group.GetSpriteIndex(0, 0, 0, 1, 0, 0, 0)] = 101;
		group.SpriteIds[group.GetSpriteIndex(0, 0, 0, 2, 0, 0, 0)] = 102;
		group.SpriteIds[group.GetSpriteIndex(0, 0, 0, 3, 0, 0, 0)] = 103;

		var snap = ThingFrameGroupEditor.CaptureSpriteLayout(group);
		group.Frames = 2;
		ThingFrameGroupEditor.RemapSpriteIdsAfterDimensionChange(group, snap);

		Assert.Equal(100u, group.GetSpriteId(0, 0, 0, 0, 0, 0, 0));
		Assert.Equal(101u, group.GetSpriteId(0, 0, 0, 1, 0, 0, 0));
		Assert.Equal(102u, group.GetSpriteId(0, 0, 0, 2, 0, 0, 0));
		Assert.Equal(103u, group.GetSpriteId(0, 0, 0, 3, 0, 0, 0));
		Assert.Equal(0u, group.GetSpriteId(0, 0, 0, 0, 0, 0, 1));
		Assert.Equal(0u, group.GetSpriteId(0, 0, 0, 3, 0, 0, 1));
	}

	[Fact]
	public void IdleAndWalkGroups_RemapIndependentlyWhenFramesChange()
	{
		var idle = OutfitWalkGroup(frames: 1, patternX: 4);
		idle.GroupTypeId = 0;
		FillDistinctIds(idle, idBase: 1000);

		var walk = OutfitWalkGroup(frames: 2, patternX: 4);
		walk.GroupTypeId = 1;
		FillDistinctIds(walk, idBase: 2000);

		var idleBefore = idle.GetSpriteId(0, 0, 0, 0, 0, 0, 0);
		var walkBefore = SnapshotIds(walk);

		var walkSnap = ThingFrameGroupEditor.CaptureSpriteLayout(walk);
		walk.Frames = 3;
		ThingFrameGroupEditor.RemapSpriteIdsAfterDimensionChange(walk, walkSnap);

		Assert.Equal(idleBefore, idle.GetSpriteId(0, 0, 0, 0, 0, 0, 0));
		Assert.Equal(1u, idle.Frames);
		Assert.Equal(walkBefore[(0, 0)], walk.GetSpriteId(0, 0, 0, 0, 0, 0, 0));
		Assert.Equal(walkBefore[(1, 3)], walk.GetSpriteId(0, 0, 0, 3, 0, 0, 1));
		Assert.Equal(0u, walk.GetSpriteId(0, 0, 0, 0, 0, 0, 2));
	}

	[Fact]
	public void EnsureFrameTimings_IncreasePreservesAndPads()
	{
		var group = OutfitWalkGroup(frames: 2, patternX: 1);
		group.IsAnimation = true;
		group.FrameTimings =
		[
			new AnimationFrameTiming(100, 110),
			new AnimationFrameTiming(200, 220),
		];

		group.Frames = 4;
		ThingFrameGroupEditor.EnsureFrameTimings(group, defaultMinimumMs: 150, defaultMaximumMs: 150);

		Assert.True(group.IsAnimation);
		Assert.Equal(4, group.FrameTimings!.Length);
		Assert.Equal(100u, group.FrameTimings[0].MinimumMilliseconds);
		Assert.Equal(200u, group.FrameTimings[1].MinimumMilliseconds);
		Assert.Equal(150u, group.FrameTimings[2].MinimumMilliseconds);
		Assert.Equal(150u, group.FrameTimings[3].MaximumMilliseconds);
	}

	[Fact]
	public void EnsureFrameTimings_DecreaseKeepsLeadingAndDropToOneClears()
	{
		var group = OutfitWalkGroup(frames: 3, patternX: 1);
		group.IsAnimation = true;
		group.FrameTimings =
		[
			new AnimationFrameTiming(100, 110),
			new AnimationFrameTiming(200, 220),
			new AnimationFrameTiming(300, 330),
		];

		group.Frames = 2;
		ThingFrameGroupEditor.EnsureFrameTimings(group, 150, 150);
		Assert.Equal(2, group.FrameTimings!.Length);
		Assert.Equal(100u, group.FrameTimings[0].MinimumMilliseconds);
		Assert.Equal(200u, group.FrameTimings[1].MinimumMilliseconds);

		group.Frames = 1;
		ThingFrameGroupEditor.EnsureFrameTimings(group, 150, 150);
		Assert.False(group.IsAnimation);
		Assert.Null(group.FrameTimings);
	}

	private static ThingFrameGroup OutfitWalkGroup(uint frames, uint patternX) => new()
	{
		GroupTypeId = 1,
		Width = 1,
		Height = 1,
		ExactSize = 32,
		Layers = 1,
		PatternX = patternX,
		PatternY = 1,
		PatternZ = 1,
		Frames = frames,
		SpriteIds = new uint[1 * 1 * patternX * 1 * 1 * frames * 1]
	};

	private static void FillDistinctIds(ThingFrameGroup group, uint idBase = 1)
	{
		uint next = idBase;
		for (uint f = 0; f < group.Frames; f++)
		for (uint z = 0; z < group.PatternZ; z++)
		for (uint y = 0; y < group.PatternY; y++)
		for (uint x = 0; x < group.PatternX; x++)
		for (uint l = 0; l < group.Layers; l++)
		for (uint h = 0; h < group.Height; h++)
		for (uint w = 0; w < group.Width; w++)
		{
			var index = group.GetSpriteIndex(w, h, l, x, y, z, f);
			group.SpriteIds[index] = next++;
		}
	}

	private static Dictionary<(uint Frame, uint PatternX), uint> SnapshotIds(ThingFrameGroup group)
	{
		var map = new Dictionary<(uint, uint), uint>();
		for (uint f = 0; f < group.Frames; f++)
		for (uint px = 0; px < group.PatternX; px++)
			map[(f, px)] = group.GetSpriteId(0, 0, 0, px, 0, 0, f);
		return map;
	}
}
