using System.Numerics;

namespace VolumetricLight.Harness;

internal sealed record HarnessPose(Vector3 Draw, Vector3 Rotation, Vector2 Zoom, Matrix4x4 Camera, bool Invert, float? PerspectiveDistance = null)
{
    const float CameraPivotDepth = 1000f;

    public static HarnessPose Flat { get; } = new(Vector3.Zero, Vector3.Zero, Vector2.One, Matrix4x4.Identity, false);

    public static Matrix4x4 CameraAt(Vector3 position, Vector3 rotationDegrees)
    {
        var translation = new Vector3(position.X, position.Y, -position.Z);
        var pivot = new Vector3(0f, 0f, CameraPivotDepth);
        return Matrix4x4.CreateTranslation(-translation)
            * Matrix4x4.CreateRotationY(MathF.PI * rotationDegrees.Y / 180f, pivot)
            * Matrix4x4.CreateRotationX(MathF.PI * rotationDegrees.X / 180f, pivot)
            * Matrix4x4.CreateRotationZ(MathF.PI * rotationDegrees.Z / 180f, pivot);
    }
}
