using System.Numerics;
using System.Windows.Media;
using Vortice;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using static VolumetricLight.ShaftCustomEffect;

namespace VolumetricLight.Tests;

[Collection("Direct2D")]
public sealed class ShaftCustomEffectTests(ITestOutputHelper output)
{
    const int GoboSize = 128;
    const int Canvas = 512;
    const float Relative = 0.03f;
    const float Absolute = 0.003f;
    const double ToleratedFraction = 0.995;

    static VolumetricLightParameters Typical => new(-90d, 55d, 10d, 300d, 100d, Colors.White, 60d, 1.5d, 30d, 0d, 40d, 40d, -90d, 0, VolumetricLightQuality.Ultra);

    static Func<int, int, Vector3> EmissionOf(int size) => (x, y) =>
    {
        const int Margin = 12;
        if (x < Margin || y < Margin || x >= size - Margin || y >= size - Margin)
            return Vector3.Zero;

        var bars = (x / 6) % 2 == 0 ? 0.9f : 0.2f;
        var dx = x - size / 2;
        var dy = y - size / 2;
        var disc = dx * dx + dy * dy < 24 * 24 ? 1f : 0.25f;
        return new Vector3(bars, y / (float)size, disc);
    };

    static Vector3 CenteredAt(int size) => new(-size / 2f, -size / 2f, 0f);

    static readonly Vector3 CenteredDraw = new(-64f, -64f, 0f);

    static DrawDescription Pose(Vector3? draw = null, Vector3? rotation = null, Matrix4x4? camera = null, Vector2? zoom = null, Vector2? center = null, bool invert = false)
        => new(draw ?? Vector3.Zero, center ?? Vector2.Zero, zoom ?? Vector2.One, rotation ?? Vector3.Zero, camera ?? Matrix4x4.Identity, InterpolationMode.Linear, 1d, invert, []);

    static Matrix4x4 OrbitCamera(Vector3 position, Vector3 rotationDegrees)
    {
        var pivot = new Vector3(0f, 0f, 1000f);
        return Matrix4x4.CreateTranslation(-new Vector3(position.X, position.Y, -position.Z))
            * Matrix4x4.CreateRotationY(MathF.PI * rotationDegrees.Y / 180f, pivot)
            * Matrix4x4.CreateRotationX(MathF.PI * rotationDegrees.X / 180f, pivot)
            * Matrix4x4.CreateRotationZ(MathF.PI * rotationDegrees.Z / 180f, pivot);
    }

    sealed class OccluderRig : IDisposable
    {
        readonly OccluderLease lease;
        readonly OccluderSlot[] slots;
        readonly ID2D1Bitmap1[] bitmaps;
        readonly OccluderLink link = new();

        public OccluderRig(IGraphicsDevicesAndContext devices, IReadOnlyList<(DrawDescription Draw, GoboPyramid Image, float Strength)> occluders)
        {
            lease = OccluderRegistry.Acquire(devices, 1);
            link.Group = lease.Group;
            LinkId = OccluderLinks.Register(link);
            slots = new OccluderSlot[occluders.Count];
            bitmaps = new ID2D1Bitmap1[occluders.Count];
            for (var index = 0; index < occluders.Count; index++)
            {
                var (draw, image, strength) = occluders[index];
                Assert.True(ViewTransform.TryCreate(draw, out var view));
                slots[index] = lease.Group.Claim()!;
                bitmaps[index] = image.CreateBitmaps(devices)[0];
                slots[index].Publish(bitmaps[index], new Occluder(view, GoboMapping.Create(draw.Zoom, draw.Invert, draw.CenterPoint), strength));
            }
        }

        public int LinkId { get; }

        public ID2D1Image GetOutput(int slot) => lease.Group.GetOutput(slot);

        public void Dispose()
        {
            foreach (var slot in slots)
                slot.Dispose();
            OccluderLinks.Unregister(LinkId);
            lease.Dispose();
            foreach (var bitmap in bitmaps)
                bitmap.Dispose();
        }
    }

    static (FloatRendering Rendering, ShaftShaderModel Model, ShaftScene Scene) Render(IGraphicsDevicesAndContext devices, VolumetricLightParameters parameters, DrawDescription draw, IReadOnlyList<(DrawDescription Draw, GoboPyramid Image, float Strength)>? occluders = null, int size = GoboSize)
    {
        var pyramid = new GoboPyramid(size, size, EmissionOf(size));
        Assert.True(ShaftScene.TryCreate(parameters, draw, new RawRectF(0f, 0f, size, size), Canvas, Canvas, 0, EffectDescriptions.Fps, out var scene));
        using var rig = new OccluderRig(devices, occluders ?? []);
        using var effect = new ShaftCustomEffect(devices);
        var bitmaps = pyramid.CreateBitmaps(devices);
        try
        {
            for (var level = 0; level < bitmaps.Length; level++)
                effect.SetInput(level, bitmaps[level], true);
            for (var slot = 0; slot < VolumetricLightSettings.OccluderCapacity; slot++)
                effect.SetInput(VolumetricLightSettings.GoboLevelCount + slot, rig.GetOutput(slot), true);
            effect.Link = rig.LinkId;
            effect.Apply(scene.Constants, default, true);
            using var image = effect.Output;
            var rendering = FloatRendering.Capture(devices, image);
            for (var index = 0; index < VolumetricLightSettings.GoboLevelCount + VolumetricLightSettings.OccluderCapacity; index++)
                effect.SetInput(index, null, true);
            var planes = (occluders ?? []).Select(occluder =>
            {
                Assert.True(ViewTransform.TryCreate(occluder.Draw, out var view));
                return (OccluderPlane.Create(scene.Constants.CameraFromLocal, new Occluder(view, GoboMapping.Create(occluder.Draw.Zoom, occluder.Draw.Invert, occluder.Draw.CenterPoint), occluder.Strength)), occluder.Image);
            }).ToArray();
            return (rendering, new ShaftShaderModel(scene.Constants, pyramid, planes), scene);
        }
        finally
        {
            foreach (var bitmap in bitmaps)
                bitmap.Dispose();
        }
    }

    void AssertFollowsTheModel(FloatRendering rendering, ShaftShaderModel model, string where)
    {
        var total = 0;
        var close = 0;
        var lit = 0;
        var worst = 0f;
        foreach (var (x, y) in rendering.Coordinates())
        {
            var actual = rendering[x, y];
            var expected = model.Shade(x, y);
            var error = MathF.Max(MathF.Abs(expected.X - actual.X), MathF.Max(MathF.Abs(expected.Y - actual.Y), MathF.Abs(expected.Z - actual.Z)));
            var scale = MathF.Max(expected.X, MathF.Max(expected.Y, expected.Z));
            total++;
            if (scale > Absolute)
                lit++;
            if (error <= Relative * scale + Absolute)
                close++;
            else
                worst = MathF.Max(worst, error / (scale + Absolute));
            Assert.Equal(0f, actual.W);
        }

        output.WriteLine($"{where}: {close}/{total} close, lit={lit}, worst={worst:F3}");
        Assert.True(lit > total / 50, $"{where} は光が少なすぎます。{lit}/{total}");
        Assert.True(close >= total * ToleratedFraction, $"{where} {close}/{total}");
    }

    [Fact]
    public void TheEffectIsEnabledOnceCreated()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var effect = new ShaftCustomEffect(context);

        Assert.True(effect.IsEnabled);
    }

    [Theory]
    [InlineData(-90d, 55d, 0d)]
    [InlineData(-90d, 55d, 12d)]
    [InlineData(30d, 40d, 25d)]
    [InlineData(-90d, 55d, -15d)]
    [InlineData(200d, -35d, 0d)]
    [InlineData(-90d, 80d, 0d)]
    public void TheShaderFollowsTheModel(double azimuth, double elevation, double spread)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();

        var (rendering, model, _) = Render(context, Typical with { Direction = azimuth, Elevation = elevation, Spread = spread }, Pose(CenteredDraw));

        AssertFollowsTheModel(rendering, model, $"{azimuth}/{elevation}/{spread}");
    }

    const int ScreenSize = 64;

    static GoboPyramid Stripes() => new(ScreenSize, ScreenSize, (x, y) => new Vector4(0f, 0f, 0f, (x / 8) % 2 == 0 ? 1f : 0f));

    static GoboPyramid Disc() => new(ScreenSize, ScreenSize, (x, y) =>
    {
        var dx = x - ScreenSize / 2;
        var dy = y - ScreenSize / 2;
        return new Vector4(0f, 0f, 0f, dx * dx + dy * dy < 24 * 24 ? 0.7f : 0f);
    });

    static (DrawDescription Draw, GoboPyramid Image, float Strength) Occlude(GoboPyramid image, Vector3 draw, Vector3? rotation = null, float strength = 1f, float zoom = 1f)
        => (Pose(draw, rotation, zoom: new Vector2(zoom)), image, strength);

    public static TheoryData<string> Shadows => new()
    {
        "stripes",
        "soft-disc",
        "two",
        "four",
        "tilted",
        "behind-the-window",
    };

    static IReadOnlyList<(DrawDescription Draw, GoboPyramid Image, float Strength)> NamedShadow(string name) => name switch
    {
        "stripes" => [Occlude(Stripes(), new Vector3(-32f, 20f, 60f))],
        "soft-disc" => [Occlude(Disc(), new Vector3(-32f, 10f, 70f), strength: 0.6f)],
        "two" => [Occlude(Stripes(), new Vector3(-32f, 10f, 40f)), Occlude(Disc(), new Vector3(-20f, 40f, 110f), strength: 0.8f)],
        "four" => [Occlude(Disc(), new Vector3(-60f, 20f, 40f)), Occlude(Disc(), new Vector3(10f, 20f, 60f)), Occlude(Stripes(), new Vector3(-30f, 50f, 90f), strength: 0.5f), Occlude(Disc(), new Vector3(-20f, 60f, 140f), strength: 0.9f)],
        "tilted" => [Occlude(Stripes(), new Vector3(-32f, 20f, 70f), new Vector3(35f, -25f, 12f))],
        _ => [Occlude(Stripes(), new Vector3(-64f, -100f, -60f), zoom: 2f)],
    };

    [Theory]
    [MemberData(nameof(Shadows))]
    public void TheShaderFollowsTheModelWithOccluders(string name)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        var occluders = NamedShadow(name);

        var (shadowed, model, _) = Render(context, Typical, Pose(CenteredDraw), occluders);
        var (clear, _, _) = Render(context, Typical, Pose(CenteredDraw));

        AssertFollowsTheModel(shadowed, model, name);
        var darker = shadowed.Coordinates().Count(point => shadowed[point.X, point.Y].X < clear[point.X, point.Y].X * 0.9f - Absolute);
        output.WriteLine($"{name}: darker={darker}");
        if (name == "behind-the-window")
            Assert.Equal(0, darker);
        else
            Assert.True(darker > 200, $"{name} {darker}");
    }

    [Fact]
    public void TheShaderFollowsTheModelWhereTheStepCountIsHeldAtItsUpperLimit()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();

        var (rendering, model, _) = Render(context, Typical with { Quality = VolumetricLightQuality.Standard, LampSize = 0d, Elevation = 25d, Length = 2500d }, Pose(CenteredAt(512)), size: 512);

        AssertFollowsTheModel(rendering, model, "upper limit");
    }

    [Fact]
    public void TheShaderFollowsTheModelWhereTheStepCountIsHeldAtItsLowerLimit()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();

        var (rendering, model, _) = Render(context, Typical with { Quality = VolumetricLightQuality.Standard, Length = 12d, Elevation = 80d }, Pose(CenteredDraw));

        AssertFollowsTheModel(rendering, model, "lower limit");
    }

    [Fact]
    public void AnOccluderOfNoStrengthOrBeyondTheSourceLeavesTheLightAsItWas()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();

        var (clear, _, _) = Render(context, Typical, Pose(CenteredDraw));
        var (weak, _, _) = Render(context, Typical, Pose(CenteredDraw), [Occlude(Stripes(), new Vector3(-32f, 20f, 60f), strength: 0f)]);
        var (behind, _, _) = Render(context, Typical, Pose(CenteredDraw), [Occlude(Stripes(), new Vector3(-64f, -100f, -60f), zoom: 2f)]);

        Assert.All(clear.Coordinates(), point =>
        {
            Assert.Equal(clear[point.X, point.Y], weak[point.X, point.Y]);
            Assert.Equal(clear[point.X, point.Y], behind[point.X, point.Y]);
        });
    }

    public static TheoryData<string> Poses => new()
    {
        "rotated",
        "orbit",
        "zoomed",
        "inverted",
        "off-center",
        "close",
        "orthographic",
        "orthographic-turned",
    };

    static DrawDescription NamedPose(string name) => name switch
    {
        "rotated" => Pose(CenteredDraw, new Vector3(25f, -35f, 10f)),
        "orbit" => Pose(CenteredDraw, camera: OrbitCamera(new Vector3(40f, -20f, 120f), new Vector3(3f, 7f, 2f))),
        "zoomed" => Pose(CenteredDraw, zoom: new Vector2(1.6f, 0.7f)),
        "inverted" => Pose(CenteredDraw, invert: true),
        "off-center" => Pose(CenteredDraw, new Vector3(0f, 20f, 0f), center: new Vector2(24f, -16f)),
        "orthographic" => Pose(new Vector3(-62.4f, -64.3f, 0f)) with { PerspectiveDistance = float.PositiveInfinity },
        "orthographic-turned" => Pose(CenteredDraw, new Vector3(0f, 0f, 30f)) with { PerspectiveDistance = float.PositiveInfinity },
        _ => Pose(new Vector3(-64f, -64f, 400f), new Vector3(0f, 0f, 15f)),
    };

    [Theory]
    [MemberData(nameof(Poses))]
    public void TheShaderFollowsTheModelUnderEveryPose(string name)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();

        var (rendering, model, _) = Render(context, Typical, NamedPose(name));

        AssertFollowsTheModel(rendering, model, name);
    }

    public static TheoryData<string> Variations => new()
    {
        "no-falloff",
        "strong-falloff",
        "forward",
        "backward",
        "isotropic",
        "wide-lamp",
        "sharp-lamp",
        "dusty",
        "dusty-wind",
        "tinted",
    };

    static VolumetricLightParameters NamedVariation(string name) => name switch
    {
        "no-falloff" => Typical with { Falloff = 0d },
        "strong-falloff" => Typical with { Falloff = 95d },
        "forward" => Typical with { Scattering = 80d },
        "backward" => Typical with { Scattering = -60d },
        "isotropic" => Typical with { Scattering = 0d },
        "wide-lamp" => Typical with { LampSize = 25d },
        "sharp-lamp" => Typical with { LampSize = 0d },
        "dusty" => Typical with { DustAmount = 70d, DustSize = 30d },
        "dusty-wind" => Typical with { DustAmount = 70d, DustSize = 30d, DustSpeed = 400d, DustDirection = 35d, Seed = 77 },
        _ => Typical with { Tint = Color.FromRgb(255, 140, 40), Intensity = 250d },
    };

    [Theory]
    [MemberData(nameof(Variations))]
    public void TheShaderFollowsTheModelForEveryLook(string name)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();

        var (rendering, model, _) = Render(context, NamedVariation(name), Pose(CenteredDraw));

        AssertFollowsTheModel(rendering, model, name);
    }
}
