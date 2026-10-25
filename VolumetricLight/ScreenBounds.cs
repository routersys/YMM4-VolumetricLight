using System.Numerics;

namespace VolumetricLight;

internal readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public bool IsEmpty => Right <= Left || Bottom <= Top;

    public static ScreenRect FromCanvas(int width, int height)
        => new((int)Math.Floor(-width / 2.0), (int)Math.Floor(-height / 2.0), (int)Math.Ceiling(width / 2.0), (int)Math.Ceiling(height / 2.0));

    public ScreenRect Intersect(ScreenRect other)
    {
        var left = Math.Max(Left, other.Left);
        var top = Math.Max(Top, other.Top);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right > left && bottom > top ? new ScreenRect(left, top, right, bottom) : default;
    }

    public ScreenRect Scale(float scale)
        => new((int)Math.Floor(Left * scale), (int)Math.Floor(Top * scale), (int)Math.Ceiling(Right * scale), (int)Math.Ceiling(Bottom * scale));

    public Vector4 ToVector() => new(Left, Top, Right, Bottom);
}

internal static class ScreenBounds
{
    private const int VolumeCornerCount = 8;

    public static ScreenRect GetVolumeRect(in ViewTransform view, in LightVolume volume, ScreenRect canvas)
    {
        Span<Vector3> corners = stackalloc Vector3[VolumeCornerCount];
        volume.GetCorners(corners);
        var minimum = new Vector2(float.MaxValue);
        var maximum = new Vector2(float.MinValue);
        foreach (var corner in corners)
        {
            if (!view.TryProject(corner, out var screen))
                return canvas;

            minimum = Vector2.Min(minimum, screen);
            maximum = Vector2.Max(maximum, screen);
        }

        if (!float.IsFinite(minimum.X + minimum.Y + maximum.X + maximum.Y))
            return canvas;

        var rect = new ScreenRect(
            (int)Math.Floor(Math.Max(minimum.X, canvas.Left)),
            (int)Math.Floor(Math.Max(minimum.Y, canvas.Top)),
            (int)Math.Ceiling(Math.Min(maximum.X, canvas.Right)),
            (int)Math.Ceiling(Math.Min(maximum.Y, canvas.Bottom)));
        return rect.IsEmpty ? default : rect;
    }
}
