using System.Numerics;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Player.Video;

namespace VolumetricLight.Tests;

public sealed class ScreenBoundsTests
{
    static readonly PlaneRect Rect = new(-200f, -100f, 200f, 100f);

    static ViewTransform View(Vector3 draw, Vector3 rotation, Matrix4x4 camera)
    {
        var description = new DrawDescription(draw, Vector2.Zero, Vector2.One, rotation, camera, InterpolationMode.Linear, 1d, false, []);
        Assert.True(ViewTransform.TryCreate(description, out var view));
        return view;
    }

    static LightVolume Volume(double azimuth, double elevation, double spread, double length)
    {
        Assert.True(LightVolume.TryCreate(Rect, VolumetricLightSettings.GetAxis(azimuth, elevation), spread, length, 1f, out var volume));
        return volume;
    }

    [Theory]
    [InlineData(1280, 720, -640, -360, 640, 360)]
    [InlineData(1281, 721, -641, -361, 641, 361)]
    [InlineData(1920, 1080, -960, -540, 960, 540)]
    public void TheCanvasIsCenteredOnTheOrigin(int width, int height, int left, int top, int right, int bottom)
    {
        Assert.Equal(new ScreenRect(left, top, right, bottom), ScreenRect.FromCanvas(width, height));
    }

    [Fact]
    public void TheIntersectionKeepsOnlyTheSharedArea()
    {
        var first = new ScreenRect(-10, -10, 20, 30);
        var second = new ScreenRect(0, -20, 50, 10);

        Assert.Equal(new ScreenRect(0, -10, 20, 10), first.Intersect(second));
        Assert.True(first.Intersect(new ScreenRect(30, 0, 40, 10)).IsEmpty);
        Assert.True(default(ScreenRect).IsEmpty);
    }

    [Fact]
    public void ScalingRoundsTheEdgesOutward()
    {
        Assert.Equal(new ScreenRect(-6, -6, 7, 9), new ScreenRect(-11, -11, 13, 17).Scale(0.5f));
        Assert.Equal(new ScreenRect(-5, -6, 7, 9), new ScreenRect(-10, -11, 13, 17).Scale(0.5f));
        Assert.Equal(new ScreenRect(-10, -11, 13, 17), new ScreenRect(-10, -11, 13, 17).Scale(1f));
    }

    [Fact]
    public void TheRectangleIsTheBoundingBoxOfTheProjectedCorners()
    {
        var view = View(new Vector3(30f, -20f, 0f), new Vector3(10f, 20f, 0f), Matrix4x4.Identity);
        var volume = Volume(-90d, 30d, 10d, 400d);
        var canvas = ScreenRect.FromCanvas(8192, 8192);
        var corners = new Vector3[8];
        volume.GetCorners(corners);
        var projected = corners.Select(corner =>
        {
            Assert.True(view.TryProject(corner, out var screen));
            return screen;
        }).ToArray();

        var rect = ScreenBounds.GetVolumeRect(view, volume, canvas);

        Assert.Equal((int)Math.Floor(projected.Min(point => point.X)), rect.Left);
        Assert.Equal((int)Math.Floor(projected.Min(point => point.Y)), rect.Top);
        Assert.Equal((int)Math.Ceiling(projected.Max(point => point.X)), rect.Right);
        Assert.Equal((int)Math.Ceiling(projected.Max(point => point.Y)), rect.Bottom);
    }

    [Fact]
    public void TheRectangleNeverLeavesTheCanvas()
    {
        var view = View(Vector3.Zero, Vector3.Zero, Matrix4x4.Identity);
        var canvas = ScreenRect.FromCanvas(320, 180);

        var rect = ScreenBounds.GetVolumeRect(view, Volume(-90d, 55d, 10d, 1200d), canvas);

        Assert.False(rect.IsEmpty);
        Assert.True(rect.Left >= canvas.Left && rect.Top >= canvas.Top && rect.Right <= canvas.Right && rect.Bottom <= canvas.Bottom);
        Assert.Equal(canvas.Bottom, rect.Bottom);
    }

    [Fact]
    public void AVolumeEntirelyOffTheCanvasHasNoRectangle()
    {
        var view = View(new Vector3(0f, -5000f, 0f), Vector3.Zero, Matrix4x4.Identity);

        var rect = ScreenBounds.GetVolumeRect(view, Volume(90d, 20d, 0d, 100d), ScreenRect.FromCanvas(1280, 720));

        Assert.True(rect.IsEmpty);
    }

    [Fact]
    public void AVolumeThatReachesBehindTheEyeCoversTheCanvas()
    {
        var view = View(Vector3.Zero, Vector3.Zero, Matrix4x4.Identity);
        var canvas = ScreenRect.FromCanvas(1280, 720);

        var rect = ScreenBounds.GetVolumeRect(view, Volume(-90d, 80d, 0d, 20000d), canvas);

        Assert.Equal(canvas, rect);
    }
}
