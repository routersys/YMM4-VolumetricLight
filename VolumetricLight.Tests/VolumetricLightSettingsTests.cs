using System.Numerics;

namespace VolumetricLight.Tests;

public sealed class VolumetricLightSettingsTests
{
    const float Tolerance = 1e-6f;

    [Theory]
    [InlineData(0d, 45d)]
    [InlineData(90d, 30d)]
    [InlineData(-135d, -60d)]
    [InlineData(200d, 10d)]
    public void TheAxisIsAUnitVector(double azimuth, double elevation)
    {
        Assert.Equal(1f, VolumetricLightSettings.GetAxis(azimuth, elevation).Length(), Tolerance);
    }

    [Fact]
    public void ZeroDegreesPointsRightAndNinetyDegreesPointsUpOnTheScreen()
    {
        var right = VolumetricLightSettings.GetAxis(0d, 45d);
        var up = VolumetricLightSettings.GetAxis(90d, 45d);

        Assert.True(right.X > 0f);
        Assert.Equal(0f, right.Y, Tolerance);
        Assert.Equal(0f, up.X, Tolerance);
        Assert.True(up.Y < 0f);
    }

    [Fact]
    public void ThePlaneComponentFollowsTheCosineOfTheElevation()
    {
        var axis = VolumetricLightSettings.GetAxis(30d, 60d);

        Assert.Equal(MathF.Sin(MathF.PI / 3f), axis.Z, Tolerance);
        Assert.Equal(0.5f * MathF.Cos(MathF.PI / 6f), axis.X, Tolerance);
        Assert.Equal(-0.5f * MathF.Sin(MathF.PI / 6f), axis.Y, Tolerance);
    }

    [Theory]
    [InlineData(35d, 35d)]
    [InlineData(-35d, -35d)]
    [InlineData(0d, 1d)]
    [InlineData(0.4d, 1d)]
    [InlineData(-0.4d, -1d)]
    [InlineData(120d, 89d)]
    [InlineData(-120d, -89d)]
    public void TheElevationKeepsAwayFromThePlaneAndFromTheAxisOfTheCamera(double elevation, double expected)
    {
        Assert.Equal(expected, VolumetricLightSettings.ClampElevationDegrees(elevation), 9);
    }

    [Theory]
    [InlineData(0d, true)]
    [InlineData(0.005d, true)]
    [InlineData(-0.005d, true)]
    [InlineData(0.01d, false)]
    [InlineData(-0.01d, false)]
    [InlineData(20d, false)]
    public void ASpreadBelowAHundredthOfADegreeIsParallel(double spread, bool expected)
    {
        Assert.Equal(expected, VolumetricLightSettings.IsParallel(spread));
    }

    [Theory]
    [InlineData(45d, 100d, 100d)]
    [InlineData(-45d, 100d, -100d)]
    [InlineData(30d, 50d, 86.60254037844386d)]
    public void TheFocalDistanceMakesTheCornerRaySpreadByTheGivenAngle(double spread, double halfDiagonal, double expected)
    {
        Assert.Equal(expected, VolumetricLightSettings.GetFocalDistance(spread, halfDiagonal), 9);
    }

    [Fact]
    public void TheSpreadIsLimitedBeforeTheTangentGrowsWithoutBound()
    {
        var limit = Math.Tan(85d * Math.PI / 180d);

        Assert.Equal(100d / limit, VolumetricLightSettings.GetFocalDistance(120d, 100d), 9);
        Assert.Equal(-100d / limit, VolumetricLightSettings.GetFocalDistance(-120d, 100d), 9);
    }

    [Theory]
    [InlineData(0d, 1000d, 0d)]
    [InlineData(50d, 1000d, -6.931471805599453e-4d)]
    [InlineData(-10d, 1000d, 0d)]
    [InlineData(99d, 1000d, -4.605170185988091e-3d)]
    [InlineData(100d, 1000d, -4.605170185988091e-3d)]
    [InlineData(60d, 0d, 0d)]
    public void TheFalloffExponentLosesTheGivenFractionOverTheLength(double percent, double length, double expected)
    {
        Assert.Equal((float)expected, VolumetricLightSettings.GetFalloffExponent(percent, length), 1e-9f);
    }

    [Fact]
    public void TheFadeCarriesTheLevelAtTheEndAndTheInverseLength()
    {
        var fade = VolumetricLightSettings.GetFade(60d, 1000d);

        Assert.Equal(0.4f, fade.X, Tolerance);
        Assert.Equal(0.001f, fade.Y, Tolerance);
        Assert.Equal(Vector4.Zero, VolumetricLightSettings.GetFade(60d, 0d) with { X = 0f });
    }

    [Theory]
    [InlineData(0d, 0d)]
    [InlineData(10d, 10d)]
    [InlineData(30d, 30d)]
    [InlineData(100d, 30d)]
    [InlineData(-5d, 0d)]
    public void ThePenumbraGrowsWithTheTangentOfTheLimitedSourceSize(double degrees, double limited)
    {
        Assert.Equal(MathF.Tan((float)(limited * Math.PI / 180d)), VolumetricLightSettings.GetPenumbraSlope(degrees), Tolerance);
    }

    [Theory]
    [InlineData(30d, 0.3f)]
    [InlineData(-30d, -0.3f)]
    [InlineData(200d, 0.95f)]
    [InlineData(-200d, -0.95f)]
    public void TheAnisotropyStaysInsideThePhaseFunctionRange(double percent, float expected)
    {
        Assert.Equal(expected, VolumetricLightSettings.GetAnisotropy(percent), Tolerance);
    }

    [Theory]
    [InlineData(100d, 500d, 0.002f)]
    [InlineData(50d, 500d, 0.001f)]
    [InlineData(5000d, 500d, 0.02f)]
    [InlineData(-10d, 500d, 0f)]
    [InlineData(100d, 0d, 0f)]
    public void TheGainFollowsTheIntensityAndTheSizeOfTheSource(double intensity, double halfDiagonal, float expected)
    {
        Assert.Equal(expected, VolumetricLightSettings.GetGain(intensity, halfDiagonal), Tolerance);
    }

    [Theory]
    [InlineData(50d, 0.5f)]
    [InlineData(-5d, 0f)]
    [InlineData(150d, 1f)]
    public void TheThresholdIsAFractionOfTheBrightnessRange(double percent, float expected)
    {
        Assert.Equal(expected, VolumetricLightSettings.GetThreshold(percent), Tolerance);
    }

    [Theory]
    [InlineData(220d, 1f / 220f)]
    [InlineData(1d, 0.125f)]
    [InlineData(1e9d, 1f / 4000f)]
    public void TheDustFrequencyIsTheInverseOfTheClampedSize(double size, float expected)
    {
        Assert.Equal(expected, VolumetricLightSettings.GetDustFrequency(size), Tolerance);
    }

    [Fact]
    public void HigherQualityLooksFinerAndWiderAndNeverRendersAtALowerResolution()
    {
        var standard = VolumetricLightSettings.GetQualityLimits(VolumetricLightQuality.Standard);
        var high = VolumetricLightSettings.GetQualityLimits(VolumetricLightQuality.High);
        var ultra = VolumetricLightSettings.GetQualityLimits(VolumetricLightQuality.Ultra);

        Assert.True(standard.StepTexels > high.StepTexels && high.StepTexels > ultra.StepTexels);
        Assert.True(standard.MaximumSteps < high.MaximumSteps && high.MaximumSteps < ultra.MaximumSteps);
        Assert.True(standard.ResolutionScale <= high.ResolutionScale && high.ResolutionScale <= ultra.ResolutionScale);
        Assert.Equal(1f, ultra.ResolutionScale);
        Assert.All(new[] { standard, high, ultra }, limits => Assert.True(limits.MaximumSteps >= VolumetricLightSettings.MinimumSteps));
    }

    [Fact]
    public void TheNearestPerspectiveDivisorIsOneOverTheLargestScreen()
    {
        Assert.Equal(1f / 8192f, VolumetricLightSettings.MinimumPerspectiveDivisor);
        Assert.Equal(-1f / 1000f, VolumetricLightSettings.BasePerspectiveSlope);
    }

    [Fact]
    public void ThePyramidHasOneLevelPerHalving()
    {
        Assert.Equal(VolumetricLightSettings.GoboLevelCount - 1, VolumetricLightSettings.MaximumGoboLevel);
        Assert.Equal(0.5f, VolumetricLightSettings.GoboLevelScale);
        Assert.Equal(6, VolumetricLightSettings.PlaneCount);
    }
}
