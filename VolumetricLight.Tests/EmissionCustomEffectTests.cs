using System.Numerics;
using YukkuriMovieMaker.Commons;
using static VolumetricLight.EmissionCustomEffect;

namespace VolumetricLight.Tests;

[Collection("Direct2D")]
public sealed class EmissionCustomEffectTests
{
    const int Width = 64;
    const int Height = 48;
    const float Relative = 0.01f;
    const float Absolute = 0.003f;
    const float MaximumChannel = 255f;
    const float MinimumLuma = 1e-6f;

    static readonly Vector3 LumaWeights = new(0.2126f, 0.7152f, 0.0722f);

    static Bgra Pattern(int x, int y)
    {
        if (x < 8)
            return Bgra.Transparent;

        var alpha = (byte)(y < Height / 2 ? byte.MaxValue : 40 + x * 3);
        return new Bgra((byte)(y * 5), (byte)(x * 4), (byte)(255 - y * 5), alpha);
    }

    static float Read(EmissionCustomEffect effect) => effect.GetFloatValue((int)PropertyIndex.Threshold);

    static FloatRendering Render(IGraphicsDevicesAndContext devices, SourceImage source, float threshold)
    {
        using var effect = new EmissionCustomEffect(devices);
        effect.SetInput(0, source.Bitmap, true);
        effect.Threshold = threshold;
        using var output = effect.Output;

        return FloatRendering.Capture(devices, output);
    }

    static Vector4 Expected(SourceImage source, int x, int y, float threshold)
    {
        var pixel = source[x, y];
        var color = new Vector4(pixel.Red, pixel.Green, pixel.Blue, pixel.Alpha) / MaximumChannel;
        if (color.W <= 0f)
            return Vector4.Zero;

        var luma = Vector3.Dot(new Vector3(color.X, color.Y, color.Z) / color.W, LumaWeights);
        return color * (MathF.Max(luma - threshold, 0f) / MathF.Max(luma, MinimumLuma));
    }

    [Fact]
    public void TheEffectIsEnabledOnceCreated()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var effect = new EmissionCustomEffect(context);

        Assert.True(effect.IsEnabled);
        Assert.Equal(0f, Read(effect));
    }

    [Theory]
    [InlineData(0.25f, 0.25f)]
    [InlineData(1f, 1f)]
    [InlineData(-0.5f, 0f)]
    [InlineData(3f, 1f)]
    [InlineData(float.NaN, 0f)]
    public void TheThresholdIsKeptWithinZeroAndOne(float value, float expected)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var effect = new EmissionCustomEffect(context);

        effect.Threshold = value;

        Assert.Equal(expected, Read(effect));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.08f)]
    [InlineData(0.4f)]
    [InlineData(0.75f)]
    public void EveryPixelKeepsItsHueAndLosesOnlyTheLumaUnderTheThreshold(float threshold)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Pattern);

        var rendering = Render(context, source, threshold);

        var close = 0;
        var bright = 0;
        foreach (var (x, y) in rendering.Coordinates())
        {
            var expected = Expected(source, x, y, threshold);
            var actual = rendering[x, y];
            var error = Vector4.Abs(expected - actual);
            var scale = MathF.Max(MathF.Max(expected.X, expected.Y), MathF.Max(expected.Z, expected.W));
            if (scale > Absolute)
                bright++;
            if (MathF.Max(MathF.Max(error.X, error.Y), MathF.Max(error.Z, error.W)) <= Relative * scale + Absolute)
                close++;
        }

        Assert.Equal(Width * Height, close);
        Assert.True(threshold >= 0.75f || bright > 0);
    }

    [Fact]
    public void AZeroThresholdPassesTheImageThrough()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Pattern);

        var rendering = Render(context, source, 0f);

        Assert.All(rendering.Coordinates(), point =>
        {
            var pixel = source[point.X, point.Y];
            var actual = rendering[point.X, point.Y];
            Assert.True(MathF.Abs(actual.X - pixel.Red / MaximumChannel) <= Absolute, $"{point} red");
            Assert.True(MathF.Abs(actual.Y - pixel.Green / MaximumChannel) <= Absolute, $"{point} green");
            Assert.True(MathF.Abs(actual.Z - pixel.Blue / MaximumChannel) <= Absolute, $"{point} blue");
            Assert.True(MathF.Abs(actual.W - pixel.Alpha / MaximumChannel) <= Absolute, $"{point} alpha");
        });
    }

    [Fact]
    public void AThresholdOfOneRemovesEverything()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Pattern);

        var rendering = Render(context, source, 1f);

        Assert.All(rendering.Coordinates(), point => Assert.Equal(Vector4.Zero, rendering[point.X, point.Y]));
    }

    [Fact]
    public void TransparentPixelsStayEmpty()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Pattern);

        var rendering = Render(context, source, 0.1f);

        Assert.All(rendering.Coordinates().Where(point => point.X < 8), point => Assert.Equal(Vector4.Zero, rendering[point.X, point.Y]));
    }

    [Fact]
    public void TheOutputCoversTheInput()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Pattern);

        var rendering = Render(context, source, 0.2f);

        Assert.Equal((0, 0, Width, Height), (rendering.Left, rendering.Top, rendering.Width, rendering.Height));
    }
}
