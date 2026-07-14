using NyxAssetsEditor.Services.Looktypes;
using Xunit;

namespace NyxAssetsEditor.Tests;

public sealed class TibiaOutfitPaletteTests
{
	[Fact]
	public void CreatesAll133ColorsInGridOrder()
	{
		var colors = TibiaOutfitPalette.Create();
		Assert.Equal(133, colors.Count);
		for (var id = 0; id < colors.Count; id++) Assert.Equal((byte)id, colors[id].Id);
	}

	[Theory]
	[InlineData(0, 255, 255, 255)]
	[InlineData(19, 218, 218, 218)]
	[InlineData(114, 36, 36, 36)]
	[InlineData(18, 255, 191, 191)]
	[InlineData(94, 255, 0, 0)]
	public void MatchesKnownOtClientSamples(int id, byte red, byte green, byte blue)
	{
		var color = TibiaOutfitPalette.Get(id);
		Assert.Equal(red, color.Red);
		Assert.Equal(green, color.Green);
		Assert.Equal(blue, color.Blue);
	}

	[Fact]
	public void OutOfRangeIdsAreClamped()
	{
		Assert.Equal(TibiaOutfitPalette.Get(0), TibiaOutfitPalette.Get(-1));
		Assert.Equal(TibiaOutfitPalette.Get(132), TibiaOutfitPalette.Get(999));
	}
}
