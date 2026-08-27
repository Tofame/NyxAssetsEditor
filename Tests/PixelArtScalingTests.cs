using NyxAssetsEditor.Services.Rendering;
using Xunit;

namespace NyxAssetsEditor.Tests;

public class PixelArtScalingTests
{
	[Theory]
	[InlineData(32, 32, 32, 32, 1)]
	[InlineData(32, 32, 96, 96, 3)]
	[InlineData(32, 32, 80, 80, 2)]
	[InlineData(64, 64, 96, 96, 1)]
	[InlineData(64, 64, 120, 120, 1)]
	[InlineData(64, 64, 32, 32, 0.5)]
	[InlineData(96, 96, 64, 64, 0.5)]
	[InlineData(128, 64, 64, 64, 0.5)]
	public void Positive_CalculateFitScale_UsesOnlyIntegerOrReciprocalIntegerRatios(
		double sourceWidth,
		double sourceHeight,
		double availableWidth,
		double availableHeight,
		double expected)
	{
		var scale = PixelArtScaling.CalculateFitScale(
			sourceWidth,
			sourceHeight,
			availableWidth,
			availableHeight);

		Assert.Equal(expected, scale);
	}

	[Theory]
	[InlineData(0, 32, 100, 100, 0.0)]
	[InlineData(32, 0, 100, 100, 0.0)]
	[InlineData(32, 32, 0, 100, 0.0)]
	[InlineData(32, 32, 100, 0, 0.0)]
	[InlineData(-10, 32, 100, 100, 0.0)]
	public void Negative_CalculateFitScale_ReturnsZeroWhenDimensionsAreNonPositive(
		double sourceWidth,
		double sourceHeight,
		double availableWidth,
		double availableHeight,
		double expected)
	{
		var scale = PixelArtScaling.CalculateFitScale(
			sourceWidth,
			sourceHeight,
			availableWidth,
			availableHeight);

		Assert.Equal(expected, scale);
	}
}
