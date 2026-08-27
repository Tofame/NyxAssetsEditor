using NyxAssetsEditor.Services.Looktypes;
using Xunit;

namespace NyxAssetsEditor.Tests;

public sealed class CharacterOutfitPaletteTests
{
	[Fact]
	public void Positive_CreatesAll133ColorsInGridOrder()
	{
		var colors = CharacterOutfitPalette.Create();
		Assert.Equal(133, colors.Count);
		for (var id = 0; id < colors.Count; id++) Assert.Equal((byte)id, colors[id].Id);
	}

	[Theory]
	[InlineData(0, 255, 255, 255)]
	[InlineData(19, 218, 218, 218)]
	[InlineData(114, 36, 36, 36)]
	[InlineData(18, 255, 191, 191)]
	[InlineData(94, 255, 0, 0)]
	public void Positive_MatchesKnownOtClientSamples(int id, byte red, byte green, byte blue)
	{
		var color = CharacterOutfitPalette.Get(id);
		Assert.Equal(red, color.Red);
		Assert.Equal(green, color.Green);
		Assert.Equal(blue, color.Blue);
	}

	[Fact]
	public void Negative_OutOfRangeIdsAreClampedToPaletteBounds()
	{
		Assert.Equal(CharacterOutfitPalette.Get(0), CharacterOutfitPalette.Get(-1));
		Assert.Equal(CharacterOutfitPalette.Get(0), CharacterOutfitPalette.Get(-100));
		Assert.Equal(CharacterOutfitPalette.Get(132), CharacterOutfitPalette.Get(999));
		Assert.Equal(CharacterOutfitPalette.Get(132), CharacterOutfitPalette.Get(133));
	}
}
