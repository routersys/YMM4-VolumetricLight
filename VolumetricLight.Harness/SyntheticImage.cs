namespace VolumetricLight.Harness;

internal static class SyntheticImage
{
    const int Samples = 4;
    const float LeadWidthFraction = 0.006f;
    const float StoneLevel = 0.03f;
    const float LeadLevel = 0.02f;
    const int ColumnsPerWindow = 3;
    const float RowHeightFraction = 0.095f;

    static readonly (float Red, float Green, float Blue)[] Palette =
    [
        (0.10f, 0.22f, 0.95f),
        (0.95f, 0.10f, 0.16f),
        (1.00f, 0.62f, 0.10f),
        (0.10f, 0.72f, 0.32f),
        (0.62f, 0.20f, 0.85f),
        (0.10f, 0.72f, 0.92f),
        (0.97f, 0.32f, 0.62f),
        (1.00f, 0.92f, 0.24f),
    ];

    static readonly (float Center, float HalfWidth, float Top)[] Windows =
    [
        (0.20f, 0.105f, 0.30f),
        (0.50f, 0.130f, 0.10f),
        (0.80f, 0.105f, 0.30f),
    ];

    const float WindowBottom = 0.93f;

    public static byte[] Create(int width, int height)
    {
        var pixels = new byte[width * height * HarnessImage.BytesPerPixel];
        var lead = LeadWidthFraction * width;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var red = 0f;
                var green = 0f;
                var blue = 0f;
                for (var sy = 0; sy < Samples; sy++)
                {
                    for (var sx = 0; sx < Samples; sx++)
                    {
                        var (r, g, b) = Shade(x + (sx + 0.5f) / Samples, y + (sy + 0.5f) / Samples, width, height, lead);
                        red += r;
                        green += g;
                        blue += b;
                    }
                }

                var scale = 1f / (Samples * Samples);
                var offset = (y * width + x) * HarnessImage.BytesPerPixel;
                pixels[offset] = ToByte(blue * scale);
                pixels[offset + 1] = ToByte(green * scale);
                pixels[offset + 2] = ToByte(red * scale);
                pixels[offset + 3] = byte.MaxValue;
            }
        }

        return pixels;
    }

    static (float Red, float Green, float Blue) Shade(float x, float y, int width, int height, float lead)
    {
        for (var index = 0; index < Windows.Length; index++)
        {
            var (centerFraction, halfFraction, topFraction) = Windows[index];
            var centerX = centerFraction * width;
            var halfWidth = halfFraction * width;
            var top = topFraction * height;
            var bottom = WindowBottom * height;
            var arcCenterY = top + halfWidth;
            if (!Inside(x, y, centerX, halfWidth, arcCenterY, bottom, out var edge))
                continue;

            if (edge < lead)
                return (LeadLevel, LeadLevel, LeadLevel);

            var cellWidth = 2f * halfWidth / ColumnsPerWindow;
            var cellHeight = RowHeightFraction * height;
            var localX = x - (centerX - halfWidth);
            var localY = y - top;
            var column = (int)MathF.Floor(localX / cellWidth);
            var row = (int)MathF.Floor(localY / cellHeight);
            var gridX = MathF.Abs(localX - MathF.Round(localX / cellWidth) * cellWidth);
            var gridY = MathF.Abs(localY - MathF.Round(localY / cellHeight) * cellHeight);
            if (MathF.Min(gridX, gridY) < lead * 0.5f)
                return (LeadLevel, LeadLevel, LeadLevel);

            var entry = Palette[(int)(Hash(column + 7 * row + 31 * index) % (uint)Palette.Length)];
            var shade = 1f - 0.35f * (y - top) / (bottom - top);
            return (entry.Red * shade, entry.Green * shade, entry.Blue * shade);
        }

        return (StoneLevel, StoneLevel, StoneLevel);
    }

    static bool Inside(float x, float y, float centerX, float halfWidth, float arcCenterY, float bottom, out float edge)
    {
        edge = 0f;
        if (y > bottom)
            return false;

        if (y >= arcCenterY)
        {
            var side = halfWidth - MathF.Abs(x - centerX);
            if (side < 0f)
                return false;

            edge = MathF.Min(side, bottom - y);
            return true;
        }

        var dx = x - centerX;
        var dy = y - arcCenterY;
        var distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance > halfWidth)
            return false;

        edge = halfWidth - distance;
        return true;
    }

    static uint Hash(int value)
    {
        var hash = (uint)value;
        hash ^= hash >> 16;
        hash *= 0x85EBCA6Bu;
        hash ^= hash >> 13;
        hash *= 0xC2B2AE35u;
        hash ^= hash >> 16;
        return hash;
    }

    static byte ToByte(float premultiplied) => (byte)MathF.Round(Math.Clamp(premultiplied, 0f, 1f) * byte.MaxValue);
}
