using System;
using NyxAssets.Things;
using NyxAssets.Things.Frames;

namespace NyxAssetsEditor.Services.Rendering;

/// <summary>Shared frame timing and progression rules for thing previews.</summary>
public static class ThingAnimationPlayback
{
	public static uint GetFrameDelayMs(ThingFrameGroup group, int frameIndex, uint fallbackMs,
		bool improvedAnimations, ThingKind kind)
	{
		if (group.FrameTimings != null && frameIndex >= 0 && frameIndex < group.FrameTimings.Length)
		{
			var timing = group.FrameTimings[frameIndex];
			var average = (timing.MinimumMilliseconds + timing.MaximumMilliseconds) / 2;
			if (average > 0)
			{
				// Preserve the established Thing Editor behavior for legacy animations and
				// outfits: their configured category duration is also the preview speed cap.
				return !improvedAnimations || kind == ThingKind.Outfit
					? Math.Min(average, fallbackMs)
					: average;
			}
		}

		return fallbackMs;
	}

	public static int GetNextFrame(int currentFrame, int startFrame, int endFrame,
		bool pingPong, ref int direction)
	{
		var end = Math.Max(0, endFrame);
		var start = Math.Clamp(startFrame, 0, end);
		var next = currentFrame + direction;

		if (pingPong)
		{
			if (next > end)
			{
				direction = -1;
				next = Math.Max(start, end - 1);
			}
			else if (next < start)
			{
				direction = 1;
				next = Math.Min(end, start + 1);
			}
		}
		else if (next > end)
		{
			next = start;
		}
		else if (next < start)
		{
			next = end;
		}

		return next;
	}
}
