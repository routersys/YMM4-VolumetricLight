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

    static readonly RawRectF Bounds = new(0f, 0f, GoboSize, GoboSize);

    static VolumetricLightParameters Typical => new(-90d, 55d, 10d, 300d, 100d, Colors.White, 60d, 1.5d, 30d, 0d, 40d, 40d, -90d, 0, VolumetricLightQuality.Ultra);

    static Vector3 Emission(int x, int y)
    {
        const int Margin = 12;
        if (x < Margin || y < Margin || x >= GoboSize - Margin || y >= GoboSize - Margin)
            return Vector3.Zero;

        var bars = (x / 6) % 2 == 0 ? 0.9f : 0.2f;
        var dx = x - GoboSize / 2;
        var dy = y - GoboSize / 2;
        var disc = dx * dx + dy * dy < 24 * 24 ? 1f : 0.25f;
        return new Vector3(bars, y / (float)GoboSize, disc);
    }

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

    static (FloatRendering Rendering, ShaftShaderModel Model, ShaftScene Scene) Render(IGraphicsDevicesAndContext devices, VolumetricLightParameters parameters, DrawDescription draw)
    {
        var pyramid = new GoboPyramid(GoboSize, GoboSize, Emission);
        Assert.True(ShaftScene.TryCreate(parameters, draw, Bounds, Canvas, Canvas, 0, EffectDescriptions.Fps, out var scene));
        using var effect = new ShaftCustomEffect(devices);
        var bitmaps = pyramid.CreateBitmaps(devices);
        try
        {
            for (var level = 0; level < bitmaps.Length; level++)
                effect.SetInput(level, bitmaps[level], true);
            effect.Apply(scene.Constants, default, true);
            using var image = effect.Output;
            var rendering = FloatRendering.Capture(devices, image);
            effect.SetInput(0, null, true);
            return (rendering, new ShaftShaderModel(scene.Constants, pyramid), scene);
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

    public static TheoryData<string> Poses => new()
    {
        "rotated",
        "orbit",
        "zoomed",
        "inverted",
        "off-center",
        "close",
    };

    static DrawDescription NamedPose(string name) => name switch
    {
        "rotated" => Pose(CenteredDraw, new Vector3(25f, -35f, 10f)),
        "orbit" => Pose(CenteredDraw, camera: OrbitCamera(new Vector3(40f, -20f, 120f), new Vector3(3f, 7f, 2f))),
        "zoomed" => Pose(CenteredDraw, zoom: new Vector2(1.6f, 0.7f)),
        "inverted" => Pose(CenteredDraw, invert: true),
        "off-center" => Pose(CenteredDraw, new Vector3(0f, 20f, 0f), center: new Vector2(24f, -16f)),
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
