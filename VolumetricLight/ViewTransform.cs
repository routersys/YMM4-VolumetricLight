using System.Numerics;
using YukkuriMovieMaker.Player.Video;

namespace VolumetricLight;

internal readonly record struct ViewTransform(Matrix4x4 CameraFromLocal, Matrix4x4 LocalFromCamera, float Slope)
{
    private const float Epsilon = 1e-6f;
    private const float HalfTurnDegrees = 180f;

    public static bool TryCreate(DrawDescription draw, out ViewTransform view)
    {
        view = default;
        var camera = FinalizeCamera(draw);
        if (!IsFinite(camera))
            return false;

        var rotationColumn = new Vector3(camera.M13, camera.M23, camera.M33);
        var lengthSquared = rotationColumn.LengthSquared();
        if (!(lengthSquared > Epsilon))
            return false;

        var projectiveSlope = Vector3.Dot(new Vector3(camera.M14, camera.M24, camera.M34), rotationColumn) / lengthSquared;
        var rigid = camera * CreateSlope(-projectiveSlope);
        var local = CreateWorld(draw.Rotation, draw.Draw) * rigid;
        var slope = VolumetricLightSettings.BasePerspectiveSlope + projectiveSlope;
        if (!IsFinite(local) || !(slope <= 0f) || !float.IsFinite(slope) || !Matrix4x4.Invert(local, out var inverse) || !IsFinite(inverse))
            return false;

        view = new ViewTransform(local, inverse, slope);
        return true;
    }

    public float GetEyeLimit()
        => Slope < 0f ? (VolumetricLightSettings.MinimumPerspectiveDivisor - 1f) / Slope : float.MaxValue;

    public bool TryProject(Vector3 local, out Vector2 screen)
    {
        var camera = Vector3.Transform(local, CameraFromLocal);
        var divisor = 1f + Slope * camera.Z;
        if (!(divisor > VolumetricLightSettings.MinimumPerspectiveDivisor))
        {
            screen = default;
            return false;
        }

        screen = new Vector2(camera.X, camera.Y) / divisor;
        return true;
    }

    public static Matrix4x4 CreateWorld(Vector3 rotation, Vector3 draw)
        => Matrix4x4.CreateRotationZ(MathF.PI * rotation.Z / HalfTurnDegrees)
            * Matrix4x4.CreateRotationY(MathF.PI * -rotation.Y / HalfTurnDegrees)
            * Matrix4x4.CreateRotationX(MathF.PI * -rotation.X / HalfTurnDegrees)
            * Matrix4x4.CreateTranslation(draw);

    public static Matrix4x4 CreateSlope(float slope)
    {
        var matrix = Matrix4x4.Identity;
        matrix.M34 = slope;
        return matrix;
    }

    public static Matrix4x4 FinalizeCamera(DrawDescription draw)
    {
        var camera = draw.Camera;
        if (draw.Billboard != BillboardMode.None && TryCreateBillboardCorrection(camera, draw.Billboard, out var correction))
        {
            var corrected = Matrix4x4.CreateTranslation(-draw.Draw) * correction * Matrix4x4.CreateTranslation(draw.Draw) * camera;
            if (IsFinite(corrected))
                camera = corrected;
        }

        if (draw.PerspectiveDistance is { } distance)
        {
            var corrected = camera * CreatePerspectiveCorrection(distance);
            if (IsFinite(corrected))
                camera = corrected;
        }

        return camera;
    }

    private static Matrix4x4 CreatePerspectiveCorrection(float distance)
    {
        if ((!float.IsFinite(distance) && !float.IsPositiveInfinity(distance)) || distance <= 0f)
            return Matrix4x4.Identity;

        var slope = -VolumetricLightSettings.BasePerspectiveSlope - 1f / distance;
        return float.IsFinite(slope) ? CreateSlope(slope) : Matrix4x4.Identity;
    }

    private static bool TryCreateBillboardCorrection(Matrix4x4 camera, BillboardMode mode, out Matrix4x4 correction)
    {
        correction = Matrix4x4.Identity;
        if (!IsFinite(camera))
            return false;

        var matrix = new Matrix4x4(
            camera.M11, camera.M12, camera.M13, 0f,
            camera.M21, camera.M22, camera.M23, 0f,
            camera.M31, camera.M32, camera.M33, 0f,
            0f, 0f, 0f, 1f);
        var determinant = matrix.GetDeterminant();
        if (!float.IsFinite(determinant) || determinant <= Epsilon || !Matrix4x4.Decompose(matrix, out var scale, out var rotation, out _)
            || !IsFinite(scale) || MathF.Abs(scale.X) <= Epsilon || MathF.Abs(scale.Y) <= Epsilon || MathF.Abs(scale.Z) <= Epsilon
            || !IsFinite(rotation) || rotation.LengthSquared() <= Epsilon)
            return false;

        var rotationMatrix = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(rotation));
        switch (mode)
        {
            case BillboardMode.Spherical:
                correction = Matrix4x4.Transpose(rotationMatrix);
                break;
            case BillboardMode.Cylindrical:
                var direction = new Vector2(rotationMatrix.M31, rotationMatrix.M33);
                if (!IsFinite(direction) || direction.LengthSquared() <= Epsilon)
                    return true;

                direction = Vector2.Normalize(direction);
                correction = Matrix4x4.CreateRotationY(-MathF.Atan2(direction.X, direction.Y));
                break;
            default:
                return false;
        }

        return IsFinite(correction);
    }

    private static bool IsFinite(Vector2 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Quaternion value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);

    internal static bool IsFinite(Matrix4x4 value)
        => float.IsFinite(value.M11) && float.IsFinite(value.M12) && float.IsFinite(value.M13) && float.IsFinite(value.M14)
            && float.IsFinite(value.M21) && float.IsFinite(value.M22) && float.IsFinite(value.M23) && float.IsFinite(value.M24)
            && float.IsFinite(value.M31) && float.IsFinite(value.M32) && float.IsFinite(value.M33) && float.IsFinite(value.M34)
            && float.IsFinite(value.M41) && float.IsFinite(value.M42) && float.IsFinite(value.M43) && float.IsFinite(value.M44);
}
