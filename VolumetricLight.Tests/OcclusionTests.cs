using System.Numerics;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Player.Video;

namespace VolumetricLight.Tests;

public sealed class OcclusionTests
{
    const float Tolerance = 2e-3f;

    static float Next(Random random, float range) => (float)(random.NextDouble() * 2.0 - 1.0) * range;

    static Matrix4x4 Camera(Random random)
        => Matrix4x4.CreateTranslation(-new Vector3(Next(random, 300f), Next(random, 300f), Next(random, 400f)))
            * Matrix4x4.CreateRotationY(MathF.PI * Next(random, 25f) / 180f, new Vector3(0f, 0f, 1000f))
            * Matrix4x4.CreateRotationX(MathF.PI * Next(random, 20f) / 180f, new Vector3(0f, 0f, 1000f));

    static ViewTransform View(DrawDescription draw)
    {
        Assert.True(ViewTransform.TryCreate(draw, out var view));
        return view;
    }

    static DrawDescription Describe(Vector3 draw, Vector3 rotation, Matrix4x4 camera, Vector2 zoom, bool invert, Vector2 center)
        => new(draw, center, zoom, rotation, camera, InterpolationMode.Linear, 1d, invert, []);

    static Occluder Create(DrawDescription draw, float strength = 1f)
        => new(View(draw), GoboMapping.Create(draw.Zoom, draw.Invert, draw.CenterPoint), strength);

    static float Dot(Vector4 row, Vector3 point) => row.X * point.X + row.Y * point.Y + row.Z * point.Z + row.W;

    [Fact]
    public void ThePlaneIsZeroOnTheOccluderAndGrowsWithTheDistanceFromIt()
    {
        var random = new Random(11);
        for (var index = 0; index < 40; index++)
        {
            var lightDraw = Describe(new Vector3(Next(random, 200f), Next(random, 200f), Next(random, 200f)), new Vector3(Next(random, 40f), Next(random, 40f), Next(random, 40f)), Camera(random), Vector2.One, false, Vector2.Zero);
            var occluderDraw = Describe(new Vector3(Next(random, 200f), Next(random, 200f), Next(random, 300f)), new Vector3(Next(random, 40f), Next(random, 40f), Next(random, 40f)), lightDraw.Camera, Vector2.One, false, Vector2.Zero);
            var light = View(lightDraw);
            var occluder = Create(occluderDraw);
            var plane = OccluderPlane.Create(light.CameraFromLocal, occluder);
            var onPlane = new Vector3(Next(random, 150f), Next(random, 150f), 0f);
            var offPlane = onPlane + new Vector3(0f, 0f, Next(random, 80f));

            var lightPointOnPlane = Vector3.Transform(Vector3.Transform(onPlane, occluder.View.CameraFromLocal), light.LocalFromCamera);
            var lightPointOffPlane = Vector3.Transform(Vector3.Transform(offPlane, occluder.View.CameraFromLocal), light.LocalFromCamera);

            Assert.True(MathF.Abs(Dot(plane.Plane, lightPointOnPlane)) < Tolerance * 10f, $"{index} on");
            Assert.True(MathF.Abs(Dot(plane.Plane, lightPointOffPlane) - offPlane.Z) < Tolerance * 10f, $"{index} off");
        }
    }

    [Theory]
    [InlineData(1f, 1f, false)]
    [InlineData(1.6f, 0.7f, false)]
    [InlineData(1.6f, 0.7f, true)]
    [InlineData(0.5f, 2f, true)]
    public void TheImagePositionIsWhereTheOccluderPixelIs(float zoomX, float zoomY, bool invert)
    {
        var random = new Random(5);
        var center = new Vector2(30f, -12f);
        for (var index = 0; index < 20; index++)
        {
            var lightDraw = Describe(new Vector3(Next(random, 100f), Next(random, 100f), 0f), new Vector3(Next(random, 30f), Next(random, 30f), 0f), Camera(random), Vector2.One, false, Vector2.Zero);
            var occluderDraw = Describe(new Vector3(Next(random, 100f), Next(random, 100f), Next(random, 100f)), new Vector3(Next(random, 30f), Next(random, 30f), Next(random, 30f)), lightDraw.Camera, new Vector2(zoomX, zoomY), invert, center);
            var light = View(lightDraw);
            var occluder = Create(occluderDraw);
            var plane = OccluderPlane.Create(light.CameraFromLocal, occluder);
            var local = new Vector3(Next(random, 120f), Next(random, 120f), 0f);
            var lightPoint = Vector3.Transform(Vector3.Transform(local, occluder.View.CameraFromLocal), light.LocalFromCamera);

            var expected = occluder.Gobo.ToImage(local.X, local.Y);

            Assert.True(MathF.Abs(Dot(plane.ImageX, lightPoint) - expected.X) < Tolerance * 10f, $"{index} x");
            Assert.True(MathF.Abs(Dot(plane.ImageY, lightPoint) - expected.Y) < Tolerance * 10f, $"{index} y");
        }
    }

    [Fact]
    public void AnOccluderSittingOnTheLightPlaneHasThePlaneEquationOfThatPlane()
    {
        var draw = Describe(new Vector3(20f, -10f, 0f), new Vector3(10f, 20f, 5f), Matrix4x4.Identity, Vector2.One, false, Vector2.Zero);
        var light = View(draw);

        var plane = OccluderPlane.Create(light.CameraFromLocal, Create(draw));

        Assert.True(MathF.Abs(Dot(plane.Plane, new Vector3(30f, 40f, 0f))) < Tolerance);
        Assert.True(MathF.Abs(Dot(plane.Plane, new Vector3(30f, 40f, 25f)) - 25f) < Tolerance);
        Assert.True(MathF.Abs(Dot(plane.ImageX, new Vector3(30f, 40f, 0f)) - 30f) < Tolerance);
        Assert.True(MathF.Abs(Dot(plane.ImageY, new Vector3(30f, 40f, 0f)) - 40f) < Tolerance);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    public void AnOccluderWithoutStrengthLeavesNoPlane(float strength)
    {
        var draw = Describe(Vector3.Zero, Vector3.Zero, Matrix4x4.Identity, Vector2.One, false, Vector2.Zero);

        Assert.Equal(default, OccluderPlane.Create(View(draw).CameraFromLocal, Create(draw, strength)));
    }

    [Fact]
    public void AnInfiniteImageMappingLeavesNoPlane()
    {
        var draw = Describe(Vector3.Zero, Vector3.Zero, Matrix4x4.Identity, Vector2.Zero, false, Vector2.Zero);

        Assert.Equal(default, OccluderPlane.Create(View(draw).CameraFromLocal, Create(draw)));
    }

    [Theory]
    [InlineData(100d, 1d, 1f)]
    [InlineData(50d, 1d, 0.5f)]
    [InlineData(100d, 0.25d, 0.25f)]
    [InlineData(50d, 0.5d, 0.25f)]
    [InlineData(250d, 1d, 1f)]
    [InlineData(-10d, 1d, 0f)]
    [InlineData(100d, 3d, 1f)]
    [InlineData(100d, -1d, 0f)]
    public void TheStrengthIsThePercentTimesTheOpacityWithinZeroAndOne(double percent, double opacity, float expected)
    {
        Assert.Equal(expected, Occluder.GetStrength(percent, opacity), 5);
    }
}
