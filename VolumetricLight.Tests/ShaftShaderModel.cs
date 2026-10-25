using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;

namespace VolumetricLight.Tests;

internal sealed class GoboPyramid
{
    const int Channels = 4;
    const float MaximumAlpha = 1f;

    readonly float[][] levels = new float[VolumetricLightSettings.GoboLevelCount][];
    readonly int[] widths = new int[VolumetricLightSettings.GoboLevelCount];
    readonly int[] heights = new int[VolumetricLightSettings.GoboLevelCount];

    public GoboPyramid(int width, int height, Func<int, int, Vector3> emission)
    {
        widths[0] = width;
        heights[0] = height;
        levels[0] = new float[width * height * Channels];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = emission(x, y);
                var offset = (y * width + x) * Channels;
                levels[0][offset] = value.X;
                levels[0][offset + 1] = value.Y;
                levels[0][offset + 2] = value.Z;
                levels[0][offset + 3] = MaximumAlpha;
            }
        }

        for (var level = 1; level < levels.Length; level++)
        {
            widths[level] = widths[level - 1] / 2;
            heights[level] = heights[level - 1] / 2;
            levels[level] = new float[widths[level] * heights[level] * Channels];
            for (var y = 0; y < heights[level]; y++)
            {
                for (var x = 0; x < widths[level]; x++)
                {
                    for (var channel = 0; channel < Channels; channel++)
                    {
                        var sum = 0f;
                        for (var dy = 0; dy < 2; dy++)
                        {
                            for (var dx = 0; dx < 2; dx++)
                                sum += levels[level - 1][((2 * y + dy) * widths[level - 1] + 2 * x + dx) * Channels + channel];
                        }

                        levels[level][(y * widths[level] + x) * Channels + channel] = sum * 0.25f;
                    }
                }
            }
        }
    }

    public int Width => widths[0];

    public int Height => heights[0];

    public Vector4 Fetch(int level, int x, int y)
    {
        if (x < 0 || y < 0 || x >= widths[level] || y >= heights[level])
            return Vector4.Zero;

        var offset = (y * widths[level] + x) * Channels;
        return new Vector4(levels[level][offset], levels[level][offset + 1], levels[level][offset + 2], levels[level][offset + 3]);
    }

    public Vector4 Bilinear(int level, float u, float v)
    {
        var x = u * widths[level] - 0.5f;
        var y = v * heights[level] - 0.5f;
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var fx = x - x0;
        var fy = y - y0;
        var top = Vector4.Lerp(Fetch(level, x0, y0), Fetch(level, x0 + 1, y0), fx);
        var bottom = Vector4.Lerp(Fetch(level, x0, y0 + 1), Fetch(level, x0 + 1, y0 + 1), fx);
        return Vector4.Lerp(top, bottom, fy);
    }

    public ID2D1Bitmap1[] CreateBitmaps(IGraphicsDevicesAndContext devices)
    {
        var format = new PixelFormat(Format.R32G32B32A32_Float, Vortice.DCommon.AlphaMode.Premultiplied);
        var bitmaps = new ID2D1Bitmap1[levels.Length];
        for (var level = 0; level < levels.Length; level++)
        {
            var stride = widths[level] * Channels * sizeof(float);
            bitmaps[level] = devices.DeviceContext.CreateBitmap(
                new SizeI(widths[level], heights[level]), nint.Zero, stride,
                new BitmapProperties1(format, SourceImage.Dpi, SourceImage.Dpi, BitmapOptions.None));
            bitmaps[level].CopyFromMemory(MemoryMarshal.AsBytes(levels[level].AsSpan()).ToArray(), stride);
        }

        return bitmaps;
    }
}

internal sealed class ShaftShaderModel(ShaftConstants constants, GoboPyramid pyramid)
{
    const float RateEpsilon = 1e-9f;
    const float UnitScale = 1f / 16777216f;
    const uint HashStep = 0x9E3779B9u;
    const uint HashMultiplyA = 0x85EBCA6Bu;
    const uint HashMultiplyB = 0xC2B2AE35u;
    const float DustSecondWeight = 0.5f;
    const float LinearFalloffLevel = 0.9999f;

    readonly ShaftConstants c = constants;
    readonly GoboPyramid pyramid = pyramid;

    static uint Mix(uint value)
    {
        value ^= value >> 16;
        value *= HashMultiplyA;
        value ^= value >> 13;
        value *= HashMultiplyB;
        value ^= value >> 16;
        return value;
    }

    static float ToUnit(uint value) => (value >> 8) * UnitScale;

    static float Lattice(int x, int y, int z, uint seed)
        => ToUnit(Mix(((uint)x * HashStep) ^ ((uint)y * HashMultiplyA) ^ ((uint)z * HashMultiplyB) ^ seed));

    static float ValueNoise(Vector3 point, uint seed)
    {
        var origin = new Vector3(MathF.Floor(point.X), MathF.Floor(point.Y), MathF.Floor(point.Z));
        var cx = (int)origin.X;
        var cy = (int)origin.Y;
        var cz = (int)origin.Z;
        var fraction = point - origin;
        var blend = fraction * fraction * (new Vector3(3f) - 2f * fraction);
        float N(int dx, int dy, int dz) => Lattice(cx + dx, cy + dy, cz + dz, seed);
        float Lerp(float a, float b, float t) => a + (b - a) * t;
        return Lerp(
            Lerp(Lerp(N(0, 0, 0), N(1, 0, 0), blend.X), Lerp(N(0, 1, 0), N(1, 1, 0), blend.X), blend.Y),
            Lerp(Lerp(N(0, 0, 1), N(1, 0, 1), blend.X), Lerp(N(0, 1, 1), N(1, 1, 1), blend.X), blend.Y),
            blend.Z);
    }

    static float Dust(Vector3 point, uint seed)
        => (ValueNoise(point, seed) + DustSecondWeight * ValueNoise(point * 2f, Mix(seed))) / (1f + DustSecondWeight);

    Vector3 Row(int index) => new(Get(c.LocalFromCamera, index, 0), Get(c.LocalFromCamera, index, 1), Get(c.LocalFromCamera, index, 2));

    static float Get(Matrix4x4 matrix, int row, int column) => row switch
    {
        0 => column switch { 0 => matrix.M11, 1 => matrix.M12, _ => matrix.M13 },
        1 => column switch { 0 => matrix.M21, 1 => matrix.M22, _ => matrix.M23 },
        2 => column switch { 0 => matrix.M31, 1 => matrix.M32, _ => matrix.M33 },
        _ => column switch { 0 => matrix.M41, 1 => matrix.M42, _ => matrix.M43 },
    };

    Vector4 Plane(int index) => index switch
    {
        0 => c.Plane0,
        1 => c.Plane1,
        2 => c.Plane2,
        3 => c.Plane3,
        4 => c.Plane4,
        _ => c.Plane5,
    };

    Vector2 ImagePosition(Vector3 source) => new(c.Gobo.X * source.X + c.Gobo.Y, c.Gobo.Z * source.Y + c.Gobo.W);

    (Vector3 Source, float Travel) FindSource(Vector3 point)
    {
        if (c.Focus.W > 0.5f)
        {
            var focus = new Vector3(c.Focus.X, c.Focus.Y, c.Focus.Z);
            var span = focus.Z - point.Z;
            var ratio = MathF.Abs(span) > RateEpsilon ? -point.Z / span : 0f;
            var toFocus = focus - point;
            return (point + toFocus * ratio, toFocus.Length() * MathF.Abs(ratio));
        }

        var axis = new Vector3(c.Axis.X, c.Axis.Y, c.Axis.Z);
        var along = point.Z / axis.Z;
        return (point - axis * along, along);
    }

    float GetLod(float divisor, float travel)
        => MathF.Log2(MathF.Max(MathF.Max(divisor, 2f * c.Beam.Y * travel) / c.March.Z, 1f));

    float GetPhase(Vector3 direction, Vector3 toEye)
    {
        var anisotropy = c.Beam.Z;
        var scatterBase = 1f + anisotropy * anisotropy - 2f * anisotropy * Vector3.Dot(direction, toEye);
        return (1f - anisotropy * anisotropy) / MathF.Sqrt(scatterBase) / scatterBase;
    }

    float GetFalloff(float travel)
    {
        if (c.Fade.X >= LinearFalloffLevel)
            return Math.Clamp(1f - travel * c.Fade.Y, 0f, 1f);

        return Math.Clamp((MathF.Exp(c.Beam.X * travel) - c.Fade.X) / (1f - c.Fade.X), 0f, 1f);
    }

    Vector4 ReadLevel(int level, Vector2 image)
    {
        var position = image * MathF.Pow(2f, -level);
        var u = position.X / (pyramid.Width >> level);
        var v = position.Y / (pyramid.Height >> level);
        if (u < 0f || u > 1f || v < 0f || v > 1f)
            return Vector4.Zero;

        return pyramid.Bilinear(level, u, v);
    }

    Vector4 SampleGobo(Vector2 image, float lod)
    {
        var clamped = Math.Clamp(lod, 0f, VolumetricLightSettings.MaximumGoboLevel);
        var lower = (int)MathF.Floor(clamped);
        var upper = Math.Min(lower + 1, VolumetricLightSettings.MaximumGoboLevel);
        var blend = clamped - lower;
        var low = ReadLevel(lower, image);
        return blend <= 0f ? low : Vector4.Lerp(low, ReadLevel(upper, image), blend);
    }

    public Vector3 Shade(int pixelX, int pixelY)
    {
        var scene = new Vector2(pixelX + 0.5f, pixelY + 0.5f);
        var footprint = c.Mode.Z;
        var screen = scene * footprint;
        var viewBase = screen.X * Row(0) + screen.Y * Row(1);
        var origin = viewBase + Row(3);
        var slope = c.View.X;
        var rayStep = slope * viewBase + Row(2);

        var zLow = -1e30f;
        var zHigh = c.March.W;
        var inside = true;
        for (var index = 0; index < VolumetricLightSettings.PlaneCount; index++)
        {
            var plane = Plane(index);
            var normal = new Vector3(plane.X, plane.Y, plane.Z);
            var numerator = Vector3.Dot(normal, origin) - plane.W;
            var rate = Vector3.Dot(normal, rayStep);
            if (MathF.Abs(rate) > RateEpsilon)
            {
                var z = -numerator / rate;
                if (rate > 0f)
                    zLow = MathF.Max(zLow, z);
                else
                    zHigh = MathF.Min(zHigh, z);
            }
            else if (numerator < 0f)
            {
                inside = false;
            }
        }

        if (!inside || zHigh <= zLow)
            return Vector3.Zero;

        var (nearSource, nearTravel) = FindSource(origin + zLow * rayStep);
        var (farSource, farTravel) = FindSource(origin + zHigh * rayStep);
        var rayLength = rayStep.Length();
        var pathLength = rayLength * (zHigh - zLow);
        var middleLod = 0.5f * (GetLod((1f + slope * zLow) * footprint, nearTravel) + GetLod((1f + slope * zHigh) * footprint, farTravel));
        var texelTravel = (ImagePosition(farSource) - ImagePosition(nearSource)).Length();
        var wanted = texelTravel * MathF.Pow(2f, -middleLod) / c.March.X;
        if (c.Dust.X > 0f)
            wanted = MathF.Max(wanted, pathLength * c.Dust.Y * c.Mode.Y);

        var count = (int)Math.Clamp(MathF.Ceiling(wanted), c.Mode.X, c.March.Y);
        var lodFloor = MathF.Log2(MathF.Max(texelTravel / count, 1f));
        var dz = (zHigh - zLow) / count;
        var stepWeight = rayLength * dz * c.Beam.W;
        var seed = (uint)c.Dust.Z;
        var cellX = (int)MathF.Floor(scene.X);
        var cellY = (int)MathF.Floor(scene.Y);
        var jitter = ToUnit(Mix(((uint)cellX * HashStep) ^ ((uint)cellY * HashMultiplyA)));
        var isPoint = c.Focus.W > 0.5f;
        var toEye = rayStep / rayLength;
        var axis = new Vector3(c.Axis.X, c.Axis.Y, c.Axis.Z);
        var focus = new Vector3(c.Focus.X, c.Focus.Y, c.Focus.Z);
        var parallelPhase = isPoint ? 0f : GetPhase(axis, toEye);
        var wind = new Vector3(c.Wind.X, c.Wind.Y, c.Wind.Z);
        var sum = Vector3.Zero;
        for (var sampleIndex = 0; sampleIndex < count; sampleIndex++)
        {
            var z = zLow + (sampleIndex + jitter) * dz;
            var volumePoint = origin + z * rayStep;
            var (sourcePoint, travel) = FindSource(volumePoint);
            var lod = MathF.Max(GetLod((1f + slope * z) * footprint, travel), lodFloor);
            var gobo = SampleGobo(ImagePosition(sourcePoint), lod);
            var phase = isPoint ? GetPhase(c.Axis.W * Vector3.Normalize(volumePoint - focus), toEye) : parallelPhase;
            var density = 1f;
            if (c.Dust.X > 0f)
                density = MathF.Max(1f + c.Dust.X * (2f * Dust((volumePoint + wind) * c.Dust.Y, seed) - 1f), 0f);

            sum += new Vector3(gobo.X, gobo.Y, gobo.Z) * (GetFalloff(travel) * phase * density);
        }

        return sum * stepWeight * new Vector3(c.Tint.X, c.Tint.Y, c.Tint.Z);
    }
}
