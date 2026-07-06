using System;
using System.Linq;
using NyxAssets.Things;

namespace NyxAssetsEditor.Services.Exchange;

/// <summary>Resizes sprite index arrays and frame timings when pattern dimensions change (Object Builder parity).</summary>
public static class ThingFrameGroupEditor
{
	public static void EnsureSpriteCapacity(ThingFrameGroup group)
	{
		var total = group.GetTotalSpriteSlots();
		if (group.SpriteIds.Length == (int)total)
			return;

		var resized = new uint[total];
		var copyLen = Math.Min(group.SpriteIds.Length, (int)total);
		if (copyLen > 0)
			Array.Copy(group.SpriteIds, resized, copyLen);
		group.SpriteIds = resized;
	}

	public static void EnsureFrameTimings(ThingFrameGroup group, uint defaultMinimumMs, uint defaultMaximumMs)
	{
		if (group.Frames <= 1)
		{
			group.IsAnimation = false;
			group.FrameTimings = null;
			return;
		}

		group.IsAnimation = true;
		if (group.FrameTimings != null && group.FrameTimings.Length == (int)group.Frames)
			return;

		var previous = group.FrameTimings;
		group.FrameTimings = new AnimationFrameTiming[group.Frames];
		for (var i = 0; i < group.Frames; i++)
		{
			group.FrameTimings[i] = previous != null && i < previous.Length
				? previous[i]
				: new AnimationFrameTiming(defaultMinimumMs, defaultMaximumMs);
		}
	}

	public static void SetDurationForAllFrames(ThingFrameGroup group, AnimationFrameTiming timing)
	{
		if (group.FrameTimings == null || group.FrameTimings.Length == 0)
			return;

		for (var i = 0; i < group.FrameTimings.Length; i++)
			group.FrameTimings[i] = timing;
	}

	public static uint GetDefaultDurationMs(ThingKind kind) => kind switch
	{
		ThingKind.Outfit => 300,
		ThingKind.Effect => 100,
		_ => 500,
	};
}
