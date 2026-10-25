using System.Numerics;

namespace VolumetricLight;

internal static class VolumetricLightSettings
{
    public const double PercentScale = 100.0;
    public const double RadiansPerDegree = Math.PI / 180.0;
    public const float BasePerspectiveSlope = -0.001f;
    public const int MaximumScreenDimension = 8192;
    public const int MaximumSourceDimension = 4096;
    public const float MinimumPerspectiveDivisor = 1f / MaximumScreenDimension;

    public const int GoboLevelCount = 6;
    public const int MaximumGoboLevel = GoboLevelCount - 1;
    public const float GoboLevelScale = 0.5f;

    public const double MinimumElevationDegrees = 1.0;
    public const double MaximumElevationDegrees = 89.0;
    public const double MinimumSpreadDegrees = 0.01;
    public const double MaximumSpreadDegrees = 85.0;
    public const double MaximumLength = 100000.0;
    public const double MaximumIntensityPercent = 1000.0;
    public const double MaximumFalloffPercent = 99.0;
    public const double MaximumAnisotropyPercent = 95.0;
    public const double MaximumLampRadiusDegrees = 30.0;
    public const double MaximumDustSize = 4000.0;
    public const double MinimumDustSize = 8.0;
    public const double MaximumDustSpeed = 5000.0;
    public const int MaximumSeed = 1000;

    public const int MinimumSteps = 16;
    public const int PlaneCount = 6;
    public const float ParallelEpsilon = 1e-6f;

    public const int OccluderCapacity = 4;
    public const int MaximumOcclusionChannel = 8;
    public const double MaximumOccluderStrengthPercent = PercentScale;

    public static QualityLimits GetQualityLimits(VolumetricLightQuality quality) => quality switch
    {
        VolumetricLightQuality.High => new QualityLimits(0.5f, 1.5, 128),
        VolumetricLightQuality.Ultra => new QualityLimits(1f, 1.0, 192),
        _ => new QualityLimits(0.5f, 3.0, 64),
    };

    public readonly record struct QualityLimits(float ResolutionScale, double StepTexels, int MaximumSteps);

    public static double ClampElevationDegrees(double elevation)
    {
        var magnitude = Math.Clamp(Math.Abs(elevation), MinimumElevationDegrees, MaximumElevationDegrees);
        return elevation < 0.0 ? -magnitude : magnitude;
    }

    public static Vector3 GetAxis(double azimuthDegrees, double elevationDegrees)
    {
        var azimuth = azimuthDegrees * RadiansPerDegree;
        var elevation = ClampElevationDegrees(elevationDegrees) * RadiansPerDegree;
        var planar = Math.Cos(elevation);
        return new Vector3((float)(planar * Math.Cos(azimuth)), (float)(-planar * Math.Sin(azimuth)), (float)Math.Sin(elevation));
    }

    public static bool IsParallel(double spreadDegrees)
        => Math.Abs(spreadDegrees) < MinimumSpreadDegrees;

    public static double GetFocalDistance(double spreadDegrees, double halfDiagonal)
    {
        var spread = Math.Clamp(spreadDegrees, -MaximumSpreadDegrees, MaximumSpreadDegrees) * RadiansPerDegree;
        return halfDiagonal / Math.Tan(spread);
    }

    public static float GetFalloffLoss(double falloffPercent)
        => (float)(Math.Clamp(falloffPercent, 0.0, MaximumFalloffPercent) / PercentScale);

    public static float GetFalloffExponent(double falloffPercent, double length)
        => length > 0.0 ? (float)(Math.Log(1.0 - GetFalloffLoss(falloffPercent)) / length) : 0f;

    public static Vector4 GetFade(double falloffPercent, double length)
        => new(1f - GetFalloffLoss(falloffPercent), length > 0.0 ? (float)(1.0 / length) : 0f, 0f, 0f);

    public static float GetPenumbraSlope(double lampRadiusDegrees)
        => (float)Math.Tan(Math.Clamp(lampRadiusDegrees, 0.0, MaximumLampRadiusDegrees) * RadiansPerDegree);

    public static float GetAnisotropy(double percent)
        => (float)(Math.Clamp(percent, -MaximumAnisotropyPercent, MaximumAnisotropyPercent) / PercentScale);

    public static float GetGain(double intensityPercent, double halfDiagonal)
        => halfDiagonal > 0.0 ? (float)(Math.Clamp(intensityPercent, 0.0, MaximumIntensityPercent) / PercentScale / halfDiagonal) : 0f;

    public static float GetThreshold(double percent)
        => (float)(Math.Clamp(percent, 0.0, PercentScale) / PercentScale);

    public static float GetDustFrequency(double size)
        => (float)(1.0 / Math.Clamp(size, MinimumDustSize, MaximumDustSize));
}
