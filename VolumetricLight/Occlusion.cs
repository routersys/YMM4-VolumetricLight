using System.Numerics;
using System.Runtime.CompilerServices;

namespace VolumetricLight;

[InlineArray(VolumetricLightSettings.OccluderCapacity)]
internal struct OccluderPlaneSet
{
    private Vector4 _first;
}

internal readonly record struct Occluder(ViewTransform View, GoboMapping Gobo, float Strength)
{
    public static float GetStrength(double percent, double opacity)
        => (float)(Math.Clamp(percent, 0.0, VolumetricLightSettings.MaximumOccluderStrengthPercent) / VolumetricLightSettings.PercentScale * Math.Clamp(opacity, 0.0, 1.0));
}

internal readonly record struct OccluderPlane(Vector4 Plane, Vector4 ImageX, Vector4 ImageY, float Strength)
{
    public static OccluderPlane Create(in Matrix4x4 cameraFromLight, in Occluder occluder)
    {
        var toOccluder = cameraFromLight * occluder.View.LocalFromCamera;
        if (!ViewTransform.IsFinite(toOccluder) || !(occluder.Strength > 0f))
            return default;

        var plane = new OccluderPlane(
            Column(toOccluder, 2),
            Column(toOccluder, 0) * occluder.Gobo.ScaleX + new Vector4(0f, 0f, 0f, occluder.Gobo.OffsetX),
            Column(toOccluder, 1) * occluder.Gobo.ScaleY + new Vector4(0f, 0f, 0f, occluder.Gobo.OffsetY),
            occluder.Strength);
        return IsFinite(plane.Plane) && IsFinite(plane.ImageX) && IsFinite(plane.ImageY) ? plane : default;
    }

    private static bool IsFinite(Vector4 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);

    private static Vector4 Column(in Matrix4x4 matrix, int index) => index switch
    {
        0 => new Vector4(matrix.M11, matrix.M21, matrix.M31, matrix.M41),
        1 => new Vector4(matrix.M12, matrix.M22, matrix.M32, matrix.M42),
        _ => new Vector4(matrix.M13, matrix.M23, matrix.M33, matrix.M43),
    };
}
