using System.Numerics;
using System.Windows.Media;
using Vortice;
using YukkuriMovieMaker.Player.Video;

namespace VolumetricLight;

internal readonly record struct VolumetricLightParameters(
    double Direction,
    double Elevation,
    double Spread,
    double Length,
    double Intensity,
    Color Tint,
    double Falloff,
    double LampSize,
    double Scattering,
    double DustAmount,
    double DustSize,
    double DustSpeed,
    double DustDirection,
    int Seed,
    VolumetricLightQuality Quality);

internal readonly record struct ShaftConstants(
    Matrix4x4 LocalFromCamera,
    Vector4 Plane0,
    Vector4 Plane1,
    Vector4 Plane2,
    Vector4 Plane3,
    Vector4 Plane4,
    Vector4 Plane5,
    Vector4 Focus,
    Vector4 Axis,
    Vector4 View,
    Vector4 Gobo,
    Vector4 Beam,
    Vector4 Tint,
    Vector4 March,
    Vector4 Dust,
    Vector4 Wind,
    Vector4 Mode,
    Vector4 Fade,
    Vector4 OutputBounds);

internal readonly record struct ShaftScene(ShaftConstants Constants, float Scale, ScreenRect Canvas, ScreenRect Shaft, ViewTransform View, LightVolume Volume)
{
    private const float DustTapsPerCell = 2f;

    public static bool TryCreate(in VolumetricLightParameters parameters, DrawDescription draw, RawRectF bounds, int canvasWidth, int canvasHeight, long frame, int fps, out ShaftScene scene)
    {
        scene = default;
        if (draw.Zoom.X == 0f || draw.Zoom.Y == 0f || draw.Opacity <= 0.0 || !ViewTransform.TryCreate(draw, out var view))
            return false;

        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        if (!(width > 0f && height > 0f && width <= VolumetricLightSettings.MaximumSourceDimension && height <= VolumetricLightSettings.MaximumSourceDimension))
            return false;

        var rect = PlaneRect.Create(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, draw.Zoom, draw.Invert, draw.CenterPoint);
        var length = Math.Clamp(parameters.Length, 0.0, VolumetricLightSettings.MaximumLength);
        var axis = VolumetricLightSettings.GetAxis(parameters.Direction, parameters.Elevation);
        var texelWorld = MathF.Sqrt(MathF.Abs(draw.Zoom.X * draw.Zoom.Y));
        if (!LightVolume.TryCreate(rect, axis, parameters.Spread, length, texelWorld, out var volume))
            return false;

        var canvas = ScreenRect.FromCanvas(canvasWidth, canvasHeight);
        var limits = VolumetricLightSettings.GetQualityLimits(parameters.Quality);
        var shaft = ScreenBounds.GetVolumeRect(view, volume, canvas).Scale(limits.ResolutionScale);
        var dustAmount = Math.Clamp(parameters.DustAmount, 0.0, VolumetricLightSettings.PercentScale) / VolumetricLightSettings.PercentScale;
        var windAngle = parameters.DustDirection * VolumetricLightSettings.RadiansPerDegree;
        var windSpeed = Math.Clamp(parameters.DustSpeed, -VolumetricLightSettings.MaximumDustSpeed, VolumetricLightSettings.MaximumDustSpeed);
        var windOffset = windSpeed * frame / fps;
        var gobo = GoboMapping.Create(draw.Zoom, draw.Invert, draw.CenterPoint);
        var constants = new ShaftConstants(
            view.LocalFromCamera,
            volume.Planes[0],
            volume.Planes[1],
            volume.Planes[2],
            volume.Planes[3],
            volume.Planes[4],
            volume.Planes[5],
            new Vector4(volume.Focus, volume.IsPoint ? 1f : 0f),
            new Vector4(volume.Axis, volume.Sign),
            new Vector4(view.Slope, 0f, 0f, 0f),
            new Vector4(gobo.ScaleX, gobo.OffsetX, gobo.ScaleY, gobo.OffsetY),
            new Vector4(
                VolumetricLightSettings.GetFalloffExponent(parameters.Falloff, length),
                VolumetricLightSettings.GetPenumbraSlope(parameters.LampSize),
                VolumetricLightSettings.GetAnisotropy(parameters.Scattering),
                VolumetricLightSettings.GetGain(parameters.Intensity, volume.HalfDiagonal)),
            new Vector4(parameters.Tint.R / 255f, parameters.Tint.G / 255f, parameters.Tint.B / 255f, 1f),
            new Vector4((float)limits.StepTexels, limits.MaximumSteps, texelWorld, view.GetEyeLimit()),
            new Vector4((float)dustAmount, VolumetricLightSettings.GetDustFrequency(parameters.DustSize), parameters.Seed, 0f),
            new Vector4((float)(Math.Cos(windAngle) * windOffset), (float)(-Math.Sin(windAngle) * windOffset), 0f, 0f),
            new Vector4(VolumetricLightSettings.MinimumSteps, DustTapsPerCell, 1f / limits.ResolutionScale, 0f),
            VolumetricLightSettings.GetFade(parameters.Falloff, length),
            shaft.ToVector());
        scene = new ShaftScene(constants, limits.ResolutionScale, canvas, shaft, view, volume);
        return true;
    }
}
