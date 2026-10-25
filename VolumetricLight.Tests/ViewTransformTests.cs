using System.IO;
using System.Numerics;
using System.Reflection;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Player.Video;

namespace VolumetricLight.Tests;

public sealed class ViewTransformTests
{
    const float Tolerance = 2e-2f;
    const float RelativeTolerance = 1e-5f;
    const int Cases = 200;
    const float NearDivisor = 0.1f;

    static readonly Matrix4x4 YmmPerspective = new(1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, -0.001f, 0f, 0f, 0f, 1f);

    static DrawDescription Describe(Vector3 draw, Vector3 rotation, Matrix4x4 camera, float? perspectiveDistance = null, BillboardMode billboard = BillboardMode.None)
        => new(draw, Vector2.Zero, Vector2.One, rotation, camera, InterpolationMode.Linear, 1d, false, [])
        {
            PerspectiveDistance = perspectiveDistance,
            Billboard = billboard,
        };

    static Matrix4x4 ReferenceWorld(Vector3 rotation, Vector3 draw)
        => Matrix4x4.CreateRotationZ(MathF.PI * rotation.Z / 180f)
            * Matrix4x4.CreateRotationY(MathF.PI * -rotation.Y / 180f)
            * Matrix4x4.CreateRotationX(MathF.PI * -rotation.X / 180f)
            * Matrix4x4.CreateTranslation(draw);

    static Vector2? Project(DrawDescription draw, Vector3 local)
    {
        var matrix = ReferenceWorld(draw.Rotation, draw.Draw) * ViewTransform.FinalizeCamera(draw) * YmmPerspective;
        var clip = Vector4.Transform(new Vector4(local, 1f), matrix);
        return clip.W > NearDivisor ? new Vector2(clip.X, clip.Y) / clip.W : null;
    }

    static Matrix4x4 Camera(Random random)
        => Matrix4x4.CreateTranslation(-new Vector3(Next(random, 400f), Next(random, 400f), Next(random, 600f)))
            * Matrix4x4.CreateRotationY(MathF.PI * Next(random, 40f) / 180f, new Vector3(0f, 0f, 1000f))
            * Matrix4x4.CreateRotationX(MathF.PI * Next(random, 30f) / 180f, new Vector3(0f, 0f, 1000f))
            * Matrix4x4.CreateRotationZ(MathF.PI * Next(random, 20f) / 180f, new Vector3(0f, 0f, 1000f));

    static float Next(Random random, float range) => (float)(random.NextDouble() * 2d - 1d) * range;

    static Vector3 NextPoint(Random random, float range) => new(Next(random, range), Next(random, range), Next(random, range / 2f));

    static void AssertNear(Vector2 expected, Vector2 actual, string label)
    {
        var allowance = Tolerance + RelativeTolerance * MathF.Max(MathF.Abs(expected.X), MathF.Abs(expected.Y));
        Assert.True(Math.Abs(expected.X - actual.X) <= allowance && Math.Abs(expected.Y - actual.Y) <= allowance, $"{label} {expected} {actual}");
    }

    [Fact]
    public void AFlatItemMapsPlanePointsToTheSamePixels()
    {
        Assert.True(ViewTransform.TryCreate(Describe(Vector3.Zero, Vector3.Zero, Matrix4x4.Identity), out var view));

        Assert.Equal(VolumetricLightSettings.BasePerspectiveSlope, view.Slope);
        Assert.True(view.TryProject(new Vector3(120f, -45f, 0f), out var screen));
        AssertNear(new Vector2(120f, -45f), screen, "flat");
    }

    [Fact]
    public void PointsTowardTheCameraAreMagnifiedByTheirPerspectiveDivisor()
    {
        Assert.True(ViewTransform.TryCreate(Describe(Vector3.Zero, Vector3.Zero, Matrix4x4.Identity), out var view));

        Assert.True(view.TryProject(new Vector3(100f, 0f, 500f), out var near));
        Assert.True(view.TryProject(new Vector3(100f, 0f, -1000f), out var far));
        AssertNear(new Vector2(200f, 0f), near, "near");
        AssertNear(new Vector2(50f, 0f), far, "far");
    }

    [Fact]
    public void PointsAtOrBehindTheEyeAreNotProjected()
    {
        Assert.True(ViewTransform.TryCreate(Describe(Vector3.Zero, Vector3.Zero, Matrix4x4.Identity), out var view));

        Assert.False(view.TryProject(new Vector3(1f, 1f, 1000f), out _));
        Assert.False(view.TryProject(new Vector3(1f, 1f, 1500f), out _));
    }

    [Fact]
    public void TheRigidTransformAndTheSlopeReproduceTheHostProjection()
    {
        var random = new Random(20261025);
        for (var index = 0; index < Cases; index++)
        {
            var draw = Describe(NextPoint(random, 300f), new Vector3(Next(random, 50f), Next(random, 50f), Next(random, 90f)), Camera(random));
            Assert.True(ViewTransform.TryCreate(draw, out var view));

            var local = NextPoint(random, 500f);
            if (Project(draw, local) is { } host)
            {
                Assert.True(view.TryProject(local, out var projected));
                AssertNear(host, projected, $"case {index}");
            }
        }
    }

    [Theory]
    [InlineData(600f)]
    [InlineData(1000f)]
    [InlineData(2500f)]
    [InlineData(float.PositiveInfinity)]
    public void ThePerspectiveDistanceBecomesTheSlope(float distance)
    {
        var random = new Random(7);
        var draw = Describe(NextPoint(random, 200f), new Vector3(10f, -20f, 5f), Camera(random), distance);

        Assert.True(ViewTransform.TryCreate(draw, out var view));

        Assert.Equal(float.IsPositiveInfinity(distance) ? 0f : -1f / distance, view.Slope, 1e-6f);
    }

    [Theory]
    [InlineData(BillboardMode.None)]
    [InlineData(BillboardMode.Spherical)]
    [InlineData(BillboardMode.Cylindrical)]
    public void BillboardingAndTheFieldOfViewKeepTheHostProjection(BillboardMode mode)
    {
        var random = new Random(31);
        for (var index = 0; index < Cases; index++)
        {
            var distance = 400f + (float)random.NextDouble() * 4000f;
            var draw = Describe(NextPoint(random, 250f), new Vector3(Next(random, 40f), Next(random, 40f), Next(random, 40f)), Camera(random), distance, mode);
            Assert.True(ViewTransform.TryCreate(draw, out var view));

            var local = NextPoint(random, 400f);
            if (Project(draw, local) is { } host)
            {
                Assert.True(view.TryProject(local, out var projected));
                AssertNear(host, projected, $"{mode} case {index}");
            }
        }
    }

    [Fact]
    public void ACameraWithItsOwnPerspectivePartIsSeparatedFromItsRigidPart()
    {
        var camera = Matrix4x4.CreateTranslation(120f, -30f, -200f) * Matrix4x4.CreateRotationY(0.4f, new Vector3(0f, 0f, 1000f)) * ViewTransform.CreateSlope(0.0004f);
        var draw = Describe(new Vector3(40f, 10f, 80f), new Vector3(15f, 25f, -10f), camera);

        Assert.True(ViewTransform.TryCreate(draw, out var view));

        Assert.Equal(VolumetricLightSettings.BasePerspectiveSlope + 0.0004f, view.Slope, 1e-6f);
        Assert.True(view.TryProject(new Vector3(70f, 20f, 30f), out var projected));
        AssertNear(Project(draw, new Vector3(70f, 20f, 30f))!.Value, projected, "perspective camera");
    }

    [Fact]
    public void TheInverseReturnsCameraSpaceToThePlane()
    {
        var random = new Random(5);
        var draw = Describe(NextPoint(random, 200f), new Vector3(10f, 30f, -15f), Camera(random));
        Assert.True(ViewTransform.TryCreate(draw, out var view));

        var local = new Vector3(33f, -77f, 12f);
        var camera = Vector3.Transform(local, view.CameraFromLocal);
        var back = Vector3.Transform(camera, view.LocalFromCamera);

        Assert.True(Vector3.Distance(local, back) < 1e-2f);
    }

    [Fact]
    public void ADegenerateCameraIsRejected()
    {
        Assert.False(ViewTransform.TryCreate(Describe(Vector3.Zero, Vector3.Zero, default), out _));
        Assert.False(ViewTransform.TryCreate(Describe(new Vector3(float.NaN, 0f, 0f), Vector3.Zero, Matrix4x4.Identity), out _));
    }

    [Fact]
    public void ThePerspectivePartThatPutsTheEyeBehindThePlaneIsRejected()
    {
        var camera = ViewTransform.CreateSlope(0.002f);

        Assert.False(ViewTransform.TryCreate(Describe(Vector3.Zero, Vector3.Zero, camera), out _));
    }

    [Theory]
    [InlineData(90f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, -1f)]
    [InlineData(-90f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f)]
    [InlineData(0f, 90f, 0f, 1f, 0f, 0f, 0f, 0f, 1f)]
    [InlineData(0f, -90f, 0f, 1f, 0f, 0f, 0f, 0f, -1f)]
    [InlineData(0f, 0f, 90f, 1f, 0f, 0f, 0f, 1f, 0f)]
    [InlineData(0f, 0f, -90f, 1f, 0f, 0f, 0f, -1f, 0f)]
    public void ARotationTurnsTheItemTheWayTheHostTurnsIt(float rotationX, float rotationY, float rotationZ, float fromX, float fromY, float fromZ, float toX, float toY, float toZ)
    {
        var turned = Vector3.Transform(new Vector3(fromX, fromY, fromZ), ViewTransform.CreateWorld(new Vector3(rotationX, rotationY, rotationZ), Vector3.Zero));

        Assert.True(Vector3.Distance(new Vector3(toX, toY, toZ), turned) < 1e-5f, $"{turned}");
    }

    [Fact]
    public void ThePositionMovesTheItemAfterItHasTurned()
    {
        var moved = Vector3.Transform(new Vector3(1f, 0f, 0f), ViewTransform.CreateWorld(new Vector3(0f, 0f, 90f), new Vector3(10f, 20f, 30f)));

        Assert.True(Vector3.Distance(new Vector3(10f, 21f, 30f), moved) < 1e-4f, $"{moved}");
    }

    [Fact]
    public void TheWorldMatrixMatchesTheHost()
    {
        if (!HostAssemblyResolver.IsConfigured)
            Assert.Skip("ホストの場所が設定されていません。");

        var type = Assembly.LoadFrom(Path.Combine(HostAssemblyResolver.Directory, "YukkuriMovieMaker.dll")).GetType("YukkuriMovieMaker.Player.Video.Effects.DrawingEffect");
        var create = type?.GetMethod("CreateWorldMatrix", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        if (create is null)
            Assert.Skip("ホストに姿勢の行列の作成が見つかりません。");

        var random = new Random(7);
        for (var index = 0; index < Cases; index++)
        {
            var rotation = new Vector3(Next(random, 180f), Next(random, 180f), Next(random, 180f));
            var draw = NextPoint(random, 300f);

            var host = (Matrix4x4)create.Invoke(null, [rotation, draw, Matrix4x4.Identity])!;
            var ours = ViewTransform.CreateWorld(rotation, draw);

            Assert.True(MaximumDifference(host, ours) < 1e-4f, $"{index}");
        }
    }

    [Fact]
    public void APointTooCloseToTheEyeIsNotProjected()
    {
        Assert.True(ViewTransform.TryCreate(Describe(Vector3.Zero, Vector3.Zero, Matrix4x4.Identity), out var view));
        var eyeDepth = -1f / view.Slope;
        var gap = (float)VolumetricLightSettings.MinimumPerspectiveDivisor / -view.Slope;

        Assert.False(view.TryProject(new Vector3(100f, 50f, eyeDepth - 0.5f * gap), out _));
        Assert.True(view.TryProject(new Vector3(100f, 50f, eyeDepth - 2f * gap), out _));
    }

    [Fact]
    public void TheFinalizedCameraMatchesTheHost()
    {
        if (!HostAssemblyResolver.IsConfigured)
            Assert.Skip("ホストの場所が設定されていません。");

        var type = Assembly.LoadFrom(Path.Combine(HostAssemblyResolver.Directory, "YukkuriMovieMaker.dll")).GetType("YukkuriMovieMaker.Player.Video.CameraFinalizer");
        var apply = type?.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static);
        if (apply is null)
            Assert.Skip("ホストにカメラの確定処理が見つかりません。");

        var random = new Random(99);
        foreach (var mode in new[] { BillboardMode.None, BillboardMode.Spherical, BillboardMode.Cylindrical })
        {
            for (var index = 0; index < Cases; index++)
            {
                float? distance = index % 3 == 0 ? null : 300f + (float)random.NextDouble() * 5000f;
                var draw = Describe(NextPoint(random, 250f), new Vector3(Next(random, 40f), Next(random, 40f), Next(random, 40f)), Camera(random), distance, mode);

                var host = (DrawDescription)apply.Invoke(null, [draw])!;
                var ours = ViewTransform.FinalizeCamera(draw);

                Assert.True(MaximumDifference(host.Camera, ours) < 1e-4f, $"{mode} {index}");
            }
        }
    }

    static float MaximumDifference(Matrix4x4 first, Matrix4x4 second)
        => new[]
        {
            first.M11 - second.M11, first.M12 - second.M12, first.M13 - second.M13, first.M14 - second.M14,
            first.M21 - second.M21, first.M22 - second.M22, first.M23 - second.M23, first.M24 - second.M24,
            first.M31 - second.M31, first.M32 - second.M32, first.M33 - second.M33, first.M34 - second.M34,
            first.M41 - second.M41, first.M42 - second.M42, first.M43 - second.M43, first.M44 - second.M44,
        }.Max(MathF.Abs);
}
