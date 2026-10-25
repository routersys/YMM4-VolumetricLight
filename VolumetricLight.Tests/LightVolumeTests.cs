using System.Numerics;

namespace VolumetricLight.Tests;

public sealed class LightVolumeTests
{
    const float Tolerance = 5e-3f;
    const float TexelSize = 1f;

    static readonly PlaneRect Rect = new(-200f, -100f, 200f, 100f);

    static LightVolume Create(double azimuth, double elevation, double spread, double length = 1000d, PlaneRect? rect = null)
    {
        var axis = VolumetricLightSettings.GetAxis(azimuth, elevation);
        Assert.True(LightVolume.TryCreate(rect ?? Rect, axis, spread, length, TexelSize, out var volume));
        return volume;
    }

    static bool IsInside(LightVolume volume, Vector3 point, float margin = 0f)
    {
        for (var index = 0; index < VolumetricLightSettings.PlaneCount; index++)
        {
            var plane = volume.Planes[index];
            if (Vector3.Dot(new Vector3(plane.X, plane.Y, plane.Z), point) - plane.W < -margin)
                return false;
        }

        return true;
    }

    public static TheoryData<double, double, double> Shapes => new()
    {
        { -90d, 55d, 0d },
        { 30d, 40d, 0d },
        { 200d, -35d, 0d },
        { -90d, 55d, 12d },
        { 45d, 20d, 30d },
        { 160d, -50d, 25d },
        { -90d, 55d, -15d },
        { 10d, 70d, -30d },
        { 0d, -45d, -20d },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void EveryRayLeavingTheRectangleStaysInsideTheVolumeUntilItsEnd(double azimuth, double elevation, double spread)
    {
        var volume = Create(azimuth, elevation, spread);
        var random = new Random(3);
        for (var index = 0; index < 500; index++)
        {
            var source = new Vector3(Rect.Left + (float)random.NextDouble() * Rect.Width, Rect.Top + (float)random.NextDouble() * Rect.Height, 0f);
            var direction = volume.GetDirection(source);
            var reach = volume.DepthSign * volume.FarDepth * (float)random.NextDouble() / direction.Z;
            var point = source + direction * reach;

            Assert.True(IsInside(volume, point, 1e-2f), $"{index} {point}");
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void PointsOutsideTheRectangleOrBeyondTheEndAreOutsideTheVolume(double azimuth, double elevation, double spread)
    {
        var volume = Create(azimuth, elevation, spread);
        var direction = volume.GetDirection(Vector3.Zero);
        var inside = direction * (volume.DepthSign * volume.FarDepth * 0.5f / direction.Z);
        Assert.True(IsInside(volume, inside));

        Assert.False(IsInside(volume, inside + direction * (volume.DepthSign * volume.FarDepth / direction.Z)));
        Assert.False(IsInside(volume, inside with { Z = -volume.DepthSign * 5f }));
        Assert.False(IsInside(volume, inside + new Vector3(5000f, 0f, 0f)));
        Assert.False(IsInside(volume, inside + new Vector3(0f, -5000f, 0f)));
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void TheSourceAndTheTravelOfAVolumePointAreTheOnesItWasBuiltFrom(double azimuth, double elevation, double spread)
    {
        var volume = Create(azimuth, elevation, spread);
        var random = new Random(11);
        for (var index = 0; index < 300; index++)
        {
            var source = new Vector3(Rect.Left + (float)random.NextDouble() * Rect.Width, Rect.Top + (float)random.NextDouble() * Rect.Height, 0f);
            var direction = volume.GetDirection(source);
            var travel = (float)random.NextDouble() * volume.FarDepth / MathF.Abs(direction.Z);
            var point = source + direction * travel;

            Assert.True(volume.TryGetSource(point, out var found, out var distance));

            Assert.True(Vector3.Distance(source, found) < Tolerance * (1f + travel * 0.01f), $"{index} {source} {found}");
            Assert.True(MathF.Abs(travel - distance) < Tolerance * (1f + travel * 0.01f), $"{index} {travel} {distance}");
        }
    }

    [Fact]
    public void AParallelVolumeKeepsItsDirectionEverywhere()
    {
        var volume = Create(-90d, 55d, 0d);

        Assert.False(volume.IsPoint);
        Assert.Equal(volume.Axis, volume.GetDirection(new Vector3(120f, -40f, 300f)));
        Assert.Equal(volume.Axis, volume.GetDirection(Vector3.Zero));
    }

    [Fact]
    public void ADivergingVolumeFansOutFromAFocusBehindThePlane()
    {
        var volume = Create(-90d, 55d, 20d);

        Assert.True(volume.IsPoint);
        Assert.Equal(1f, volume.Sign);
        Assert.True(volume.Focus.Z < 0f);
        var left = volume.GetDirection(new Vector3(-200f, 0f, 0f));
        var right = volume.GetDirection(new Vector3(200f, 0f, 0f));
        Assert.True(left.X < right.X);
    }

    [Fact]
    public void AConvergingVolumeAimsAtAFocusInFrontOfThePlane()
    {
        var volume = Create(-90d, 55d, -20d);

        Assert.True(volume.IsPoint);
        Assert.Equal(-1f, volume.Sign);
        Assert.True(volume.Focus.Z > 0f);
        var left = volume.GetDirection(new Vector3(-200f, 0f, 0f));
        var right = volume.GetDirection(new Vector3(200f, 0f, 0f));
        Assert.True(left.X > right.X);
    }

    [Fact]
    public void AConvergingVolumeEndsBeforeItsFocus()
    {
        var volume = Create(-90d, 55d, -20d, 1_000_000d);

        Assert.True(volume.FarDepth < MathF.Abs(volume.Focus.Z));
        Assert.True(volume.FarDepth > 0.99f * MathF.Abs(volume.Focus.Z));
    }

    [Fact]
    public void TheLengthIsMeasuredAlongTheAxisAndLimitsTheDepth()
    {
        var volume = Create(0d, 30d, 0d, 800d);

        Assert.Equal(800f * MathF.Sin(MathF.PI / 6f), volume.FarDepth, Tolerance);
    }

    [Fact]
    public void ABeamPointingAwayFromTheCameraLiesBehindThePlane()
    {
        var volume = Create(0d, -40d, 0d);
        var corners = new Vector3[8];

        volume.GetCorners(corners);

        Assert.Equal(-1f, volume.DepthSign);
        Assert.All(corners[4..], corner => Assert.Equal(-volume.FarDepth, corner.Z, Tolerance));
    }

    [Fact]
    public void TheCornersAreTheRectangleAndItsProjectionAtTheEnd()
    {
        var volume = Create(-90d, 55d, 0d);
        var corners = new Vector3[8];

        volume.GetCorners(corners);

        Assert.Equal(new Vector3(Rect.Left, Rect.Top, 0f), corners[0]);
        Assert.Equal(new Vector3(Rect.Right, Rect.Top, 0f), corners[1]);
        Assert.Equal(new Vector3(Rect.Right, Rect.Bottom, 0f), corners[2]);
        Assert.Equal(new Vector3(Rect.Left, Rect.Bottom, 0f), corners[3]);
        for (var index = 0; index < 4; index++)
        {
            Assert.Equal(volume.FarDepth, corners[4 + index].Z, Tolerance);
            Assert.True(Vector3.Distance(corners[4 + index] - corners[index], volume.Axis * (volume.FarDepth / volume.Axis.Z)) < Tolerance);
        }
    }

    [Fact]
    public void AnEmptyRectangleHasNoVolume()
    {
        Assert.False(LightVolume.TryCreate(new PlaneRect(0f, 0f, 1000f, 0f), VolumetricLightSettings.GetAxis(0d, 45d), 0d, 1000d, TexelSize, out _));
        Assert.False(LightVolume.TryCreate(new PlaneRect(0f, 0f, 0f, 1000f), VolumetricLightSettings.GetAxis(0d, 45d), 0d, 1000d, TexelSize, out _));
    }

    [Fact]
    public void ALengthOfZeroHasNoVolume()
    {
        Assert.False(LightVolume.TryCreate(Rect, VolumetricLightSettings.GetAxis(0d, 45d), 0d, 0d, TexelSize, out _));
    }

    [Fact]
    public void AnAxisLyingInThePlaneHasNoVolume()
    {
        Assert.False(LightVolume.TryCreate(Rect, new Vector3(1f, 0f, 0f), 0d, 1000d, TexelSize, out _));
    }

    [Fact]
    public void AFocusSoCloseThatNothingRemainsHasNoVolume()
    {
        var tiny = new PlaneRect(-0.4f, -0.4f, 0.4f, 0.4f);

        Assert.False(LightVolume.TryCreate(tiny, VolumetricLightSettings.GetAxis(0d, 45d), -30d, 1000d, 2f, out _));
    }

    [Fact]
    public void TheRectangleMapsThroughTheZoomAndTheFlip()
    {
        var plain = PlaneRect.Create(-100f, -50f, 100f, 50f, new Vector2(2f, 3f), false, Vector2.Zero);
        var mirrored = PlaneRect.Create(-100f, -50f, 100f, 50f, new Vector2(2f, 3f), true, new Vector2(10f, 0f));
        var negative = PlaneRect.Create(-100f, -50f, 100f, 50f, new Vector2(-1f, -1f), false, Vector2.Zero);

        Assert.Equal(new PlaneRect(-200f, -150f, 200f, 150f), plain);
        Assert.Equal(new PlaneRect(-160f, -150f, 240f, 150f), mirrored);
        Assert.Equal(new PlaneRect(-100f, -50f, 100f, 50f), negative);
        Assert.Equal(250f, plain.HalfDiagonal, 1e-3f);
        Assert.Equal(new Vector3(0f, 0f, 0f), plain.Center);
        Assert.True(plain.IsValid);
        Assert.False(new PlaneRect(0f, 0f, float.PositiveInfinity, 10f).IsValid);
    }

    [Theory]
    [InlineData(1f, false, 0f)]
    [InlineData(2f, false, 0f)]
    [InlineData(2f, true, 10f)]
    [InlineData(-1.5f, true, -7f)]
    public void TheGoboMappingUndoesTheZoomAndTheFlip(float zoom, bool invert, float center)
    {
        var mapping = GoboMapping.Create(new Vector2(zoom, zoom), invert, new Vector2(center, 0f));
        var rect = PlaneRect.Create(-30f, -20f, 70f, 40f, new Vector2(zoom, zoom), invert, new Vector2(center, 0f));
        var corners = new[]
        {
            (rect.Left, rect.Top),
            (rect.Right, rect.Top),
            (rect.Right, rect.Bottom),
            (rect.Left, rect.Bottom),
        };

        var images = corners.Select(corner => mapping.ToImage(corner.Item1, corner.Item2)).ToArray();

        Assert.Equal(-30f, images.Min(image => image.X), Tolerance);
        Assert.Equal(70f, images.Max(image => image.X), Tolerance);
        Assert.Equal(-20f, images.Min(image => image.Y), Tolerance);
        Assert.Equal(40f, images.Max(image => image.Y), Tolerance);
    }
}
