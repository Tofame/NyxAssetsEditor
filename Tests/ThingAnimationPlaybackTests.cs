using NyxAssetsEditor.Services.Rendering;
using Xunit;

namespace NyxAssetsEditor.Tests;

public sealed class ThingAnimationPlaybackTests
{
	[Fact]
	public void LoopingUsesConfiguredStartFrame()
	{
		var direction = 1;
		Assert.Equal(2, ThingAnimationPlayback.GetNextFrame(4, 2, 4, false, ref direction));
	}

	[Fact]
	public void ExistingEditorBoundaryBehaviorIsPreservedBeforeStartFrame()
	{
		var direction = 1;
		Assert.Equal(4, ThingAnimationPlayback.GetNextFrame(0, 2, 4, false, ref direction));
	}

	[Fact]
	public void PingPongReversesAtBothEnds()
	{
		var direction = 1;
		Assert.Equal(3, ThingAnimationPlayback.GetNextFrame(4, 0, 4, true, ref direction));
		Assert.Equal(-1, direction);
		Assert.Equal(1, ThingAnimationPlayback.GetNextFrame(0, 0, 4, true, ref direction));
		Assert.Equal(1, direction);
	}
}
