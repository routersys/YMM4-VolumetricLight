using System.Numerics;
using System.Windows.Media;
using Vortice;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Player.Video;

namespace VolumetricLight.Tests;

public sealed class ShaftSceneTests
{
    const float Tolerance = 1e-5f;
    const int Width = 1280;
    const int Height = 720;
    const int Fps = 30;

    static readonly RawRectF Bounds = new(-480f, -270f, 480f, 270f);

    static VolumetricLightParameters Typical => new(-90d, 55d, 10d, 1200d, 100d, Colors.White, 60d, 1.5d, 30d, 0d, 220d, 40d, -90d, 0, VolumetricLightQuality.Standard);

    static DrawDescription Draw(Vector3? position = null, Vector2? zoom = null, bool invert = false, double opacity = 1d)
        => new(position ?? Vector3.Zero, Vector2.Zero, zoom ?? Vector2.One, Vector3.Zero, Matrix4x4.Identity, InterpolationMode.Linear, opacity, invert, []);

    static ShaftScene Create(VolumetricLightParameters parameters, DrawDescription? draw = null, long frame = 0)
    {
        Assert.True(ShaftScene.TryCreate(parameters, draw ?? Draw(), Bounds, Width, Height, frame, Fps, out var scene));
        return scene;
    }

    [Fact]
    public void ATypicalItemProducesAScene()
    {
        var scene = Create(Typical);

        Assert.Equal(0.5f, scene.Scale);
        Assert.Equal(ScreenRect.FromCanvas(Width, Height), scene.Canvas);
        Assert.False(scene.Shaft.IsEmpty);
        Assert.Equal(-0.001f, scene.Constants.View.X);
        Assert.Equal(1f, scene.Constants.Mode.Z * 0.5f);
    }

    [Fact]
    public void TheVolumeAndTheViewAreCarriedIntoTheConstants()
    {
        var scene = Create(Typical);
        var volume = scene.Volume;

        Assert.Equal(new Vector4(volume.Focus, 1f), scene.Constants.Focus);
        Assert.Equal(new Vector4(volume.Axis, 1f), scene.Constants.Axis);
        Assert.Equal(volume.Planes[0], scene.Constants.Plane0);
        Assert.Equal(volume.Planes[5], scene.Constants.Plane5);
        Assert.Equal(scene.View.LocalFromCamera, scene.Constants.LocalFromCamera);
    }

    [Fact]
    public void AParallelBeamHasNoFocus()
    {
        var scene = Create(Typical with { Spread = 0d });

        Assert.Equal(0f, scene.Constants.Focus.W);
        Assert.Equal(1f, scene.Constants.Axis.W);
    }

    [Fact]
    public void AConvergingBeamCarriesAMinusSign()
    {
        var scene = Create(Typical with { Spread = -20d });

        Assert.Equal(1f, scene.Constants.Focus.W);
        Assert.Equal(-1f, scene.Constants.Axis.W);
    }

    [Fact]
    public void TheBeamConstantsAreTheSettingsDerivations()
    {
        var scene = Create(Typical);
        var volume = scene.Volume;

        Assert.Equal(VolumetricLightSettings.GetFalloffExponent(60d, 1200d), scene.Constants.Beam.X);
        Assert.Equal(VolumetricLightSettings.GetPenumbraSlope(1.5d), scene.Constants.Beam.Y);
        Assert.Equal(VolumetricLightSettings.GetAnisotropy(30d), scene.Constants.Beam.Z);
        Assert.Equal(VolumetricLightSettings.GetGain(100d, volume.HalfDiagonal), scene.Constants.Beam.W);
        Assert.Equal(VolumetricLightSettings.GetFade(60d, 1200d), scene.Constants.Fade);
    }

    [Fact]
    public void TheMarchStepsFollowTheQualityAndTheEyeLimit()
    {
        var standard = Create(Typical).Constants.March;
        var ultra = Create(Typical with { Quality = VolumetricLightQuality.Ultra }).Constants.March;

        Assert.Equal(3f, standard.X);
        Assert.Equal(64f, standard.Y);
        Assert.Equal(1f, standard.Z);
        Assert.Equal(1f, ultra.X);
        Assert.Equal(192f, ultra.Y);
        Assert.Equal((1f / 8192f - 1f) / -0.001f, standard.W, 1f);
    }

    [Fact]
    public void ThePhysicalResolutionIsHalfAtStandardAndFullAtUltra()
    {
        var standard = Create(Typical);
        var ultra = Create(Typical with { Quality = VolumetricLightQuality.Ultra });

        Assert.Equal(0.5f, standard.Scale);
        Assert.Equal(2f, standard.Constants.Mode.Z);
        Assert.Equal(1f, ultra.Scale);
        Assert.Equal(1f, ultra.Constants.Mode.Z);
        Assert.True(ultra.Shaft.Right - ultra.Shaft.Left >= 2 * (standard.Shaft.Right - standard.Shaft.Left) - 2);
        Assert.Equal(VolumetricLightSettings.MinimumSteps, standard.Constants.Mode.X);
    }

    [Fact]
    public void TheTintIsPassedAsAFractionOfEachChannel()
    {
        var scene = Create(Typical with { Tint = Color.FromRgb(255, 128, 0) });

        Assert.Equal(1f, scene.Constants.Tint.X);
        Assert.Equal(128f / 255f, scene.Constants.Tint.Y, Tolerance);
        Assert.Equal(0f, scene.Constants.Tint.Z);
    }

    [Theory]
    [InlineData(0d, 30L, 100d, 100f, 0f)]
    [InlineData(90d, 30L, 100d, 0f, -100f)]
    [InlineData(180d, 60L, 50d, -100f, 0f)]
    [InlineData(0d, 0L, 100d, 0f, 0f)]
    [InlineData(0d, 30L, 1e9d, 5000f, 0f)]
    public void TheDustDriftsAlongItsDirectionAtItsSpeed(double direction, long frame, double speed, float expectedX, float expectedY)
    {
        var scene = Create(Typical with { DustDirection = direction, DustSpeed = speed }, frame: frame);

        Assert.Equal(expectedX, scene.Constants.Wind.X, 1e-3f);
        Assert.Equal(expectedY, scene.Constants.Wind.Y, 1e-3f);
    }

    [Fact]
    public void TheDustSeedAndAmountAreClamped()
    {
        var scene = Create(Typical with { DustAmount = 250d, Seed = 7, DustSize = 100d });

        Assert.Equal(1f, scene.Constants.Dust.X);
        Assert.Equal(0.01f, scene.Constants.Dust.Y, Tolerance);
        Assert.Equal(7f, scene.Constants.Dust.Z);
        Assert.Equal(0f, Create(Typical with { DustAmount = -5d }).Constants.Dust.X);
    }

    [Fact]
    public void TheGoboMappingUsesTheZoomAndTheFlip()
    {
        var scene = Create(Typical, Draw(zoom: new Vector2(2f, 4f), invert: true));

        Assert.Equal(-0.5f, scene.Constants.Gobo.X);
        Assert.Equal(0f, scene.Constants.Gobo.Y);
        Assert.Equal(0.25f, scene.Constants.Gobo.Z);
        Assert.Equal(0f, scene.Constants.Gobo.W);
    }

    [Fact]
    public void ThePositionMovesTheVolumeOnTheScreen()
    {
        var centered = Create(Typical);
        var shifted = Create(Typical, Draw(new Vector3(100f, 0f, 0f)));

        Assert.Equal(centered.Constants.March.Z, shifted.Constants.March.Z);
        Assert.NotEqual(centered.Constants.LocalFromCamera, shifted.Constants.LocalFromCamera);
    }

    [Theory]
    [InlineData(0.0f, 1.0f, 1d)]
    [InlineData(1.0f, 0.0f, 1d)]
    [InlineData(1.0f, 1.0f, 0d)]
    public void AnInvisibleItemHasNoScene(float zoomX, float zoomY, double opacity)
    {
        Assert.False(ShaftScene.TryCreate(Typical, Draw(zoom: new Vector2(zoomX, zoomY), opacity: opacity), Bounds, Width, Height, 0, Fps, out _));
    }

    [Fact]
    public void AnEmptyOrOversizedSourceHasNoScene()
    {
        Assert.False(ShaftScene.TryCreate(Typical, Draw(), new RawRectF(0f, 0f, 0f, 100f), Width, Height, 0, Fps, out _));
        Assert.False(ShaftScene.TryCreate(Typical, Draw(), new RawRectF(0f, 0f, 100f, -5f), Width, Height, 0, Fps, out _));
        Assert.False(ShaftScene.TryCreate(Typical, Draw(), new RawRectF(-3000f, 0f, 3000f, 100f), Width, Height, 0, Fps, out _));
        Assert.False(ShaftScene.TryCreate(Typical, Draw(), new RawRectF(float.MinValue, float.MinValue, float.MaxValue, float.MaxValue), Width, Height, 0, Fps, out _));
    }

    [Fact]
    public void ABeamOfNoLengthHasNoScene()
    {
        Assert.False(ShaftScene.TryCreate(Typical with { Length = 0d }, Draw(), Bounds, Width, Height, 0, Fps, out _));
    }

    [Fact]
    public void TheLengthIsLimited()
    {
        var limited = Create(Typical with { Length = 1e12d });
        var maximum = Create(Typical with { Length = VolumetricLightSettings.MaximumLength });

        Assert.Equal(maximum.Volume.FarDepth, limited.Volume.FarDepth);
        Assert.Equal(maximum.Constants.Fade, limited.Constants.Fade);
    }
}
