using System.Drawing;
using System.Globalization;
using System.Numerics;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Json;
using YukkuriMovieMaker.Player.Video;

namespace VolumetricLight.Tests;

[Collection("Direct2D")]
public sealed class VolumetricLightEffectProcessorTests
{
    const int Width = 96;
    const int Height = 64;
    const int Length = 30;
    const int Tolerance = 3;
    const int MarkedFrame = 10;
    const int GlassMargin = 6;
    const int GlassPeriod = 32;
    const byte GlassLevel = 110;

    static readonly Size Canvas = new(320, 240);

    static Bgra Glass(int x, int y)
    {
        var column = x % GlassPeriod;
        if (column < GlassMargin || column >= GlassPeriod - GlassMargin || y < GlassMargin || y >= Height - GlassMargin)
            return Bgra.Opaque(8, 8, 8);

        return (x / GlassPeriod) switch
        {
            0 => Bgra.Opaque(20, 30, GlassLevel),
            1 => Bgra.Opaque(30, GlassLevel, 40),
            _ => Bgra.Opaque(GlassLevel, 60, 20),
        };
    }

    static VolumetricLightEffect Effect(Action<VolumetricLightEffect>? configure = null)
    {
        var effect = new VolumetricLightEffect();
        configure?.Invoke(effect);
        return effect;
    }

    static VolumetricLightEffect Dusty()
        => Effect(effect =>
        {
            effect.DustAmount.Values[0].Value = 60d;
            effect.DustSpeed.Values[0].Value = 200d;
        });

    static Animation Linear(double from, double to)
        => Json.LoadFromText<Animation>(string.Create(CultureInfo.InvariantCulture, $$"""{"AnimationType":"直線移動","Values":[{"Value":{{from}}},{"Value":{{to}}}]}"""))!;

    static Rendering RenderFrame(IGraphicsDevicesAndContext devices, IVideoEffectProcessor processor, int frame, DrawDescription? draw = null)
    {
        processor.Update(EffectDescriptions.At(frame, Length, draw ?? EffectDescriptions.Flat, Canvas));
        return Rendering.Capture(devices, processor.Output);
    }

    static Rendering RenderOnce(IGraphicsDevicesAndContext devices, VolumetricLightEffect effect, int frame, DrawDescription? draw = null, Func<int, int, Bgra>? pixels = null)
    {
        using var source = new SourceImage(devices, Width, Height, pixels ?? Glass);
        using var processor = effect.CreateVideoEffect(devices);
        processor.SetInput(source.Bitmap);
        return RenderFrame(devices, processor, frame, draw);
    }

    static bool IsLit(Rendering rendering, int x, int y)
    {
        var pixel = rendering[x, y];
        return pixel.Red != 0 || pixel.Green != 0 || pixel.Blue != 0;
    }

    static int LitPixels(Rendering rendering) => rendering.Coordinates().Count(point => IsLit(rendering, point.X, point.Y));

    static bool InsideSource(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    static void AssertClose(Bgra expected, Bgra actual, string where)
    {
        Assert.True(Math.Abs(expected.Blue - actual.Blue) <= Tolerance, $"{where} blue {expected} {actual}");
        Assert.True(Math.Abs(expected.Green - actual.Green) <= Tolerance, $"{where} green {expected} {actual}");
        Assert.True(Math.Abs(expected.Red - actual.Red) <= Tolerance, $"{where} red {expected} {actual}");
        Assert.True(Math.Abs(expected.Alpha - actual.Alpha) <= Tolerance, $"{where} alpha {expected} {actual}");
    }

    static DrawDescription Posed(Vector3 draw, Vector3 rotation) => EffectDescriptions.Flat with { Draw = draw, Rotation = rotation };

    [Fact]
    public void TheProcessorBakesThePoseIntoTheScreen()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Glass);
        using var processor = Effect().CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);
        var posed = EffectDescriptions.Flat with
        {
            Draw = new Vector3(10f, 20f, 30f),
            Rotation = new Vector3(5f, 6f, 7f),
            Zoom = new Vector2(1.2f, 0.9f),
            Invert = true,
            Opacity = 0.5d,
        };

        var baked = processor.Update(EffectDescriptions.At(0, Length, posed, Canvas));

        Assert.Equal(posed with
        {
            Draw = Vector3.Zero,
            Zoom = Vector2.One,
            Rotation = Vector3.Zero,
            Camera = Matrix4x4.Identity,
            PerspectiveDistance = null,
            Billboard = BillboardMode.None,
            Invert = false,
        }, baked);
    }

    [Theory]
    [InlineData(0f, 0f, 1d)]
    [InlineData(1f, 0f, 1d)]
    [InlineData(0f, 1f, 1d)]
    [InlineData(1f, 1f, 0d)]
    public void ADegeneratePoseLeavesTheItemUntouched(float zoomX, float zoomY, double opacity)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Glass);
        using var processor = Effect().CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);
        var degenerate = EffectDescriptions.Flat with { Zoom = new Vector2(zoomX, zoomY), Opacity = opacity };

        var draw = processor.Update(EffectDescriptions.At(0, Length, degenerate, Canvas));
        var rendering = Rendering.Capture(context, processor.Output);

        Assert.Equal(degenerate, draw);
        Assert.Equal((0, 0, Width, Height), (rendering.Left, rendering.Top, rendering.Width, rendering.Height));
        Assert.All(rendering.Coordinates(), point => Assert.Equal(source[point.X, point.Y], rendering[point.X, point.Y]));
    }

    [Fact]
    public void ASourceBeyondTheSizeLimitPassesThrough()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        const int Oversize = VolumetricLightSettings.MaximumSourceDimension + 1;
        using var source = new SourceImage(context, Oversize, 4, (x, _) => x % 2 == 0 ? Bgra.Opaque(200, 200, 200) : Bgra.Transparent);
        using var processor = Effect().CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var draw = processor.Update(EffectDescriptions.At(0, Length, EffectDescriptions.Flat, Canvas));
        var rendering = Rendering.Capture(context, processor.Output);

        Assert.Equal(EffectDescriptions.Flat, draw);
        Assert.Equal((0, 0, Oversize, 4), (rendering.Left, rendering.Top, rendering.Width, rendering.Height));
        Assert.All(Enumerable.Range(0, 64), x => Assert.Equal(source[x, 1], rendering[x, 1]));
    }

    [Fact]
    public void WithoutLightTheSourceIsReproducedWhereItStands()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Glass);
        using var processor = Effect(effect => effect.Intensity.Values[0].Value = 0d).CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var rendering = RenderFrame(context, processor, 0);

        Assert.Equal((0, 0, Width, Height), (rendering.Left, rendering.Top, rendering.Width, rendering.Height));
        Assert.All(rendering.Coordinates(), point => AssertClose(source[point.X, point.Y], rendering[point.X, point.Y], $"{point}"));
    }

    [Fact]
    public void ALightBelowTheThresholdLeavesOnlyTheSource()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();

        var dark = RenderOnce(context, Effect(effect => effect.Threshold.Values[0].Value = 100d), 0);
        var off = RenderOnce(context, Effect(effect => effect.Intensity.Values[0].Value = 0d), 0);

        Assert.All(dark.Coordinates(), point =>
        {
            if (InsideSource(point.X, point.Y))
                Assert.Equal(off[point.X, point.Y], dark[point.X, point.Y]);
            else
                Assert.False(IsLit(dark, point.X, point.Y), $"{point}");
        });
    }

    [Fact]
    public void TheShaftsReachBeyondTheSourceAndNeverLeaveTheCanvas()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();

        var rendering = RenderOnce(context, Effect(), 0);

        var outside = rendering.Coordinates().Count(point => !InsideSource(point.X, point.Y) && IsLit(rendering, point.X, point.Y));
        Assert.True(outside > Width * Height / 2, $"{outside}");
        Assert.True(rendering.Left >= -Canvas.Width / 2 && rendering.Top >= -Canvas.Height / 2);
        Assert.True(rendering.Right <= Canvas.Width / 2 && rendering.Bottom <= Canvas.Height / 2);
    }

    [Fact]
    public void HidingTheSourceKeepsTheShaftsAndRemovesOnlyTheSource()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();

        var shown = RenderOnce(context, Effect(), 0);
        var hidden = RenderOnce(context, Effect(effect => effect.ShowSource = false), 0);

        Assert.True(LitPixels(hidden) > Width * Height / 2);
        Assert.All(hidden.Coordinates().Where(point => !InsideSource(point.X, point.Y)), point => AssertClose(shown[point.X, point.Y], hidden[point.X, point.Y], $"{point}"));
        Assert.All(Enumerable.Range(GlassMargin, Width - 2 * GlassMargin).Where(x => x % GlassPeriod == GlassPeriod / 2), x =>
        {
            var y = Height / 2;
            var expected = Glass(x, y).Premultiplied();
            Assert.True(shown[x, y].Red - hidden[x, y].Red >= expected.Red - Tolerance - 1, $"{x} red");
            Assert.Equal(byte.MaxValue, shown[x, y].Alpha);
            Assert.Equal(0, hidden[x, y].Alpha);
        });
    }

    [Fact]
    public void TheBakedSourceFillsTheProjectedQuadOfARotatedItem()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        var posed = Posed(new Vector3(-30f, -20f, 0f), new Vector3(10f, 40f, -15f));
        Assert.True(ViewTransform.TryCreate(posed, out var view));
        var corners = new[] { new Vector3(0f, 0f, 0f), new Vector3(Width, 0f, 0f), new Vector3(Width, Height, 0f), new Vector3(0f, Height, 0f) };
        var projected = corners.Select(corner =>
        {
            Assert.True(view.TryProject(corner, out var screen));
            return screen;
        }).ToArray();

        var rendering = RenderOnce(context, Effect(effect => effect.Intensity.Values[0].Value = 0d), 0, posed);

        var covered = rendering.Coordinates().Where(point => rendering[point.X, point.Y].Alpha > 0).ToArray();
        Assert.NotEmpty(covered);
        Assert.InRange(covered.Min(point => point.X), projected.Min(point => point.X) - 2d, projected.Min(point => point.X) + 2d);
        Assert.InRange(covered.Max(point => point.X) + 1, projected.Max(point => point.X) - 2d, projected.Max(point => point.X) + 2d);
        Assert.InRange(covered.Min(point => point.Y), projected.Min(point => point.Y) - 2d, projected.Min(point => point.Y) + 2d);
        Assert.InRange(covered.Max(point => point.Y) + 1, projected.Max(point => point.Y) - 2d, projected.Max(point => point.Y) + 2d);
    }

    [Fact]
    public void TheLightFollowsTheItemWhenItIsRotatedOrMoved()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();

        var flat = RenderOnce(context, Effect(), 0);
        var rotated = RenderOnce(context, Effect(), 0, Posed(Vector3.Zero, new Vector3(0f, 35f, 0f)));
        var moved = RenderOnce(context, Effect(), 0, Posed(new Vector3(40f, 30f, 0f), Vector3.Zero));

        Assert.False(flat.SamePixelsAs(rotated));
        Assert.False(flat.SamePixelsAs(moved));
        Assert.True(LitPixels(rotated) > Width * Height / 2);
        Assert.True(LitPixels(moved) > Width * Height / 2);
    }

    [Fact]
    public void ReturningToAFrameReproducesItExactly()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Glass);
        using var processor = Dusty().CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var first = RenderFrame(context, processor, MarkedFrame);
        var other = RenderFrame(context, processor, 2 * MarkedFrame);
        var again = RenderFrame(context, processor, MarkedFrame);

        Assert.False(first.SamePixelsAs(other));
        Assert.True(first.SamePixelsAs(again));
    }

    [Fact]
    public void ANewProcessorDrawsTheSameLightAsOneThatAlreadyDrewAnotherFrame()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Glass);
        using var used = Dusty().CreateVideoEffect(context);
        used.SetInput(source.Bitmap);
        RenderFrame(context, used, 0);
        using var fresh = Dusty().CreateVideoEffect(context);
        fresh.SetInput(source.Bitmap);

        var reused = RenderFrame(context, used, MarkedFrame);
        var created = RenderFrame(context, fresh, MarkedFrame);

        Assert.True(reused.SamePixelsAs(created));
    }

    public static TheoryData<string> Settings => new()
    {
        "direction",
        "elevation",
        "spread",
        "length",
        "intensity",
        "threshold",
        "tint",
        "falloff",
        "lamp",
        "scattering",
        "source",
        "dust-amount",
        "dust-size",
        "dust-speed",
        "dust-direction",
        "seed",
        "quality",
    };

    static void Change(string setting, VolumetricLightEffect effect)
    {
        switch (setting)
        {
            case "direction": effect.Direction.Values[0].Value = -45d; break;
            case "elevation": effect.Elevation.Values[0].Value = 30d; break;
            case "spread": effect.Spread.Values[0].Value = -12d; break;
            case "length": effect.Length.Values[0].Value = 500d; break;
            case "intensity": effect.Intensity.Values[0].Value = 220d; break;
            case "threshold": effect.Threshold.Values[0].Value = 30d; break;
            case "tint": effect.Tint = System.Windows.Media.Color.FromRgb(255, 140, 40); break;
            case "falloff": effect.Falloff.Values[0].Value = 10d; break;
            case "lamp": effect.LampSize.Values[0].Value = 12d; break;
            case "scattering": effect.Scattering.Values[0].Value = -50d; break;
            case "source": effect.ShowSource = false; break;
            case "dust-amount": effect.DustAmount.Values[0].Value = 100d; break;
            case "dust-size": effect.DustSize.Values[0].Value = 60d; break;
            case "dust-speed": effect.DustSpeed.Values[0].Value = -300d; break;
            case "dust-direction": effect.DustDirection.Values[0].Value = 20d; break;
            case "seed": effect.Seed = 123; break;
            default: effect.Quality = VolumetricLightQuality.Ultra; break;
        }
    }

    [Theory]
    [MemberData(nameof(Settings))]
    public void EverySettingChangedAfterTheFirstFrameReachesTheShader(string setting)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Glass);
        var effect = Dusty();
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);
        var before = RenderFrame(context, processor, MarkedFrame);

        Change(setting, effect);
        var after = RenderFrame(context, processor, MarkedFrame);

        var expected = Dusty();
        Change(setting, expected);
        var fresh = RenderOnce(context, expected, MarkedFrame);
        Assert.False(before.SamePixelsAs(after), $"{setting} unchanged");
        Assert.True(after.SamePixelsAs(fresh), $"{setting} differs from a new processor");
    }

    public static TheoryData<string> PoseChanges => new()
    {
        "zoom",
        "invert",
        "move",
        "rotate",
        "camera",
    };

    static DrawDescription ChangedPose(string name) => name switch
    {
        "zoom" => EffectDescriptions.Flat with { Zoom = new Vector2(1.4f, 0.8f) },
        "invert" => EffectDescriptions.Flat with { Invert = true },
        "move" => EffectDescriptions.Flat with { Draw = new Vector3(-30f, 20f, 40f) },
        "rotate" => EffectDescriptions.Flat with { Rotation = new Vector3(10f, 30f, 5f) },
        _ => EffectDescriptions.Flat with { Camera = Matrix4x4.CreateTranslation(-20f, 10f, 60f) },
    };

    [Theory]
    [MemberData(nameof(PoseChanges))]
    public void EveryPoseChangedAfterTheFirstFrameReachesTheLight(string name)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Glass);
        using var processor = Effect().CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);
        var before = RenderFrame(context, processor, 0);

        var after = RenderFrame(context, processor, 0, ChangedPose(name));

        var fresh = RenderOnce(context, Effect(), 0, ChangedPose(name));
        Assert.False(before.SamePixelsAs(after), name);
        Assert.True(after.SamePixelsAs(fresh), name);
    }

    [Fact]
    public void AFrameThatCannotBeDrawnLeavesNoTraceOnTheFramesAroundIt()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Glass);
        using var processor = Effect().CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);
        var first = RenderFrame(context, processor, 0);

        var hidden = RenderFrame(context, processor, 1, EffectDescriptions.Flat with { Opacity = 0d });
        var again = RenderFrame(context, processor, 0);

        Assert.Equal((0, 0, Width, Height), (hidden.Left, hidden.Top, hidden.Width, hidden.Height));
        Assert.All(hidden.Coordinates(), point => Assert.Equal(source[point.X, point.Y], hidden[point.X, point.Y]));
        Assert.True(first.SamePixelsAs(again));
    }

    [Fact]
    public void AnimatedParametersAreReadAtEachFrame()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Glass);
        var animated = new VolumetricLightEffect();
        animated.Direction.CopyFrom(Linear(-120d, -60d));
        animated.Intensity.CopyFrom(Linear(60d, 160d));
        using var processor = animated.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var first = RenderFrame(context, processor, 0);
        var middle = RenderFrame(context, processor, Length / 2);

        var constant = Effect(effect =>
        {
            effect.Direction.Values[0].Value = animated.Direction.GetValue(Length / 2, Length, EffectDescriptions.Fps);
            effect.Intensity.Values[0].Value = animated.Intensity.GetValue(Length / 2, Length, EffectDescriptions.Fps);
        });
        var expected = RenderOnce(context, constant, Length / 2);
        Assert.False(first.SamePixelsAs(middle));
        Assert.True(middle.SamePixelsAs(expected));
    }

    [Fact]
    public void TheDustDriftsOnlyWhenItHasSpeedAndAmount()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();

        var standing = Effect(effect => { effect.DustAmount.Values[0].Value = 60d; effect.DustSpeed.Values[0].Value = 0d; });
        var drifting = Dusty();
        var clear = Effect(effect => effect.DustSpeed.Values[0].Value = 200d);

        Assert.True(RenderOnce(context, standing, 0).SamePixelsAs(RenderOnce(context, standing, MarkedFrame)));
        Assert.False(RenderOnce(context, drifting, 0).SamePixelsAs(RenderOnce(context, drifting, MarkedFrame)));
        Assert.True(RenderOnce(context, clear, 0).SamePixelsAs(RenderOnce(context, clear, MarkedFrame)));
    }

    [Fact]
    public void TheSeedChangesTheDustAndOnlyTheDust()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();

        var plain = RenderOnce(context, Effect(), 0);
        var plainSeeded = RenderOnce(context, Effect(effect => effect.Seed = 55), 0);
        var dust = RenderOnce(context, Effect(effect => effect.DustAmount.Values[0].Value = 80d), 0);
        var dustSeeded = RenderOnce(context, Effect(effect => { effect.DustAmount.Values[0].Value = 80d; effect.Seed = 55; }), 0);

        Assert.True(plain.SamePixelsAs(plainSeeded));
        Assert.False(dust.SamePixelsAs(dustSeeded));
    }

    [Fact]
    public void EveryQualityDrawsTheShaftsWithinTheCanvas()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();

        foreach (var quality in Enum.GetValues<VolumetricLightQuality>())
        {
            var rendering = RenderOnce(context, Effect(effect => effect.Quality = quality), 0);

            Assert.True(LitPixels(rendering) > Width * Height, $"{quality}");
            Assert.True(rendering.Left >= -Canvas.Width / 2 && rendering.Right <= Canvas.Width / 2 && rendering.Top >= -Canvas.Height / 2 && rendering.Bottom <= Canvas.Height / 2, $"{quality}");
        }
    }

    [Fact]
    public void ALengthOfZeroLeavesTheItemUntouched()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Glass);
        using var processor = Effect(effect => effect.Length.Values[0].Value = 0d).CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var draw = processor.Update(EffectDescriptions.At(0, Length, EffectDescriptions.Flat, Canvas));
        var rendering = Rendering.Capture(context, processor.Output);

        Assert.Equal(EffectDescriptions.Flat, draw);
        Assert.Equal((0, 0, Width, Height), (rendering.Left, rendering.Top, rendering.Width, rendering.Height));
        Assert.All(rendering.Coordinates(), point => Assert.Equal(source[point.X, point.Y], rendering[point.X, point.Y]));
    }

    [Fact]
    public void ASourceFarOffTheCanvasLeavesNothingToDraw()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Glass);
        using var processor = Effect().CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);
        var far = Posed(new Vector3(0f, -9000f, 0f), Vector3.Zero);

        var draw = processor.Update(EffectDescriptions.At(0, Length, far, Canvas));

        Assert.Equal(Vector3.Zero, draw.Draw);
        Assert.Equal(Vector2.One, draw.Zoom);
    }

    [Fact]
    public void AFailureWhileUpdatingIsNotSwallowed()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var processor = Effect().CreateVideoEffect(context);

        Assert.ThrowsAny<Exception>(() => processor.Update(null!));
    }

    [Fact]
    public void AFailureWhileCreatingTheProcessorIsNotSwallowed()
    {
        Assert.ThrowsAny<Exception>(() => Effect().CreateVideoEffect(null!));
    }
}
