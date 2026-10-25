using System.Numerics;
using System.Runtime.CompilerServices;

namespace VolumetricLight;

[InlineArray(VolumetricLightSettings.PlaneCount)]
internal struct PlaneSet
{
    private Vector4 _first;
}

internal readonly record struct PlaneRect(float Left, float Top, float Right, float Bottom)
{
    public float Width => Right - Left;

    public float Height => Bottom - Top;

    public float HalfDiagonal => 0.5f * MathF.Sqrt(Width * Width + Height * Height);

    public Vector3 Center => new(0.5f * (Left + Right), 0.5f * (Top + Bottom), 0f);

    public static PlaneRect Create(float left, float top, float right, float bottom, Vector2 zoom, bool invert, Vector2 center)
    {
        var mappedLeft = zoom.X * (invert ? 2f * center.X - left : left);
        var mappedRight = zoom.X * (invert ? 2f * center.X - right : right);
        var mappedTop = zoom.Y * top;
        var mappedBottom = zoom.Y * bottom;
        return new PlaneRect(
            MathF.Min(mappedLeft, mappedRight),
            MathF.Min(mappedTop, mappedBottom),
            MathF.Max(mappedLeft, mappedRight),
            MathF.Max(mappedTop, mappedBottom));
    }

    public bool IsValid => Width > 0f && Height > 0f && float.IsFinite(Width) && float.IsFinite(Height);
}

internal readonly record struct GoboMapping(float ScaleX, float OffsetX, float ScaleY, float OffsetY)
{
    public static GoboMapping Create(Vector2 zoom, bool invert, Vector2 center)
    {
        var scaleX = 1f / zoom.X;
        var scaleY = 1f / zoom.Y;
        return invert
            ? new GoboMapping(-scaleX, 2f * center.X, scaleY, 0f)
            : new GoboMapping(scaleX, 0f, scaleY, 0f);
    }

    public Vector2 ToImage(float x, float y) => new(ScaleX * x + OffsetX, ScaleY * y + OffsetY);
}

internal readonly record struct LightVolume(
    bool IsPoint,
    Vector3 Axis,
    Vector3 Focus,
    float Sign,
    float DepthSign,
    float FarDepth,
    float HalfDiagonal,
    PlaneSet Planes,
    PlaneRect Rect)
{
    private const int CornerCount = 4;

    public static bool TryCreate(PlaneRect rect, Vector3 axis, double spreadDegrees, double length, float texelSize, out LightVolume volume)
    {
        volume = default;
        if (!rect.IsValid || !(length > 0.0) || !(MathF.Abs(axis.Z) > VolumetricLightSettings.ParallelEpsilon))
            return false;

        var depthSign = axis.Z >= 0f ? 1f : -1f;
        var halfDiagonal = rect.HalfDiagonal;
        var center = rect.Center;
        var isPoint = !VolumetricLightSettings.IsParallel(spreadDegrees);
        var focus = Vector3.Zero;
        var sign = 1f;
        var farDepth = (float)Math.Min(length * Math.Abs(axis.Z), (double)float.MaxValue);
        if (isPoint)
        {
            var distance = VolumetricLightSettings.GetFocalDistance(spreadDegrees, halfDiagonal);
            focus = center - axis * (float)distance;
            sign = distance > 0.0 ? 1f : -1f;
            if (sign < 0f)
                farDepth = MathF.Min(farDepth, MathF.Abs(focus.Z) * (1f - texelSize / halfDiagonal));
        }

        if (!(farDepth > 0f) || !float.IsFinite(farDepth))
            return false;

        var planes = new PlaneSet();
        Span<Vector3> corners = stackalloc Vector3[CornerCount]
        {
            new Vector3(rect.Left, rect.Top, 0f),
            new Vector3(rect.Right, rect.Top, 0f),
            new Vector3(rect.Right, rect.Bottom, 0f),
            new Vector3(rect.Left, rect.Bottom, 0f),
        };
        for (var index = 0; index < CornerCount; index++)
        {
            var start = corners[index];
            var end = corners[(index + 1) % CornerCount];
            var normal = isPoint ? Vector3.Cross(start - focus, end - focus) : Vector3.Cross(end - start, axis);
            if (!(normal.LengthSquared() > 0f))
                return false;

            normal = Vector3.Normalize(normal);
            if (Vector3.Dot(normal, center - start) < 0f)
                normal = -normal;
            planes[index] = new Vector4(normal, Vector3.Dot(normal, start));
        }

        planes[CornerCount] = new Vector4(0f, 0f, depthSign, 0f);
        planes[CornerCount + 1] = new Vector4(0f, 0f, -depthSign, -farDepth);
        volume = new LightVolume(isPoint, axis, focus, sign, depthSign, farDepth, halfDiagonal, planes, rect);
        return true;
    }

    public Vector3 GetDirection(Vector3 point)
        => IsPoint ? Sign * Vector3.Normalize(point - Focus) : Axis;

    public bool TryGetSource(Vector3 point, out Vector3 source, out float travel)
    {
        if (IsPoint)
        {
            var span = Focus.Z - point.Z;
            if (MathF.Abs(span) <= VolumetricLightSettings.ParallelEpsilon)
            {
                source = default;
                travel = 0f;
                return false;
            }

            var ratio = -point.Z / span;
            source = point + (Focus - point) * ratio;
            travel = (Focus - point).Length() * MathF.Abs(ratio);
            return true;
        }

        var distance = point.Z / Axis.Z;
        source = point - Axis * distance;
        travel = distance;
        return true;
    }

    public void GetCorners(Span<Vector3> corners)
    {
        var rect = Rect;
        for (var index = 0; index < CornerCount; index++)
        {
            var near = new Vector3(index is 0 or 3 ? rect.Left : rect.Right, index < 2 ? rect.Top : rect.Bottom, 0f);
            var direction = GetDirection(near);
            var far = near + direction * (DepthSign * FarDepth / direction.Z);
            corners[index] = near;
            corners[CornerCount + index] = far;
        }
    }
}
