using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;

namespace VolumetricLight.Tests;

internal sealed class FloatRendering
{
    public const int Channels = 4;

    static readonly PixelFormat PixelFormat = new(Format.R32G32B32A32_Float, Vortice.DCommon.AlphaMode.Premultiplied);

    readonly float[] pixels;

    public int Left { get; }

    public int Top { get; }

    public int Width { get; }

    public int Height { get; }

    FloatRendering(int left, int top, int width, int height, float[] pixels)
    {
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        this.pixels = pixels;
    }

    public static FloatRendering Capture(IGraphicsDevicesAndContext devices, ID2D1Image image)
    {
        var context = devices.DeviceContext;
        var bounds = context.GetImageLocalBounds(image);
        var left = (int)Math.Floor(bounds.Left);
        var top = (int)Math.Floor(bounds.Top);
        var width = (int)Math.Ceiling(bounds.Right) - left;
        var height = (int)Math.Ceiling(bounds.Bottom) - top;
        var stride = width * Channels * sizeof(float);
        var size = new SizeI(width, height);

        using var target = context.CreateBitmap(size, nint.Zero, stride, new BitmapProperties1(PixelFormat, SourceImage.Dpi, SourceImage.Dpi, BitmapOptions.Target));
        using var readback = context.CreateBitmap(size, nint.Zero, stride, new BitmapProperties1(PixelFormat, SourceImage.Dpi, SourceImage.Dpi, BitmapOptions.CpuRead | BitmapOptions.CannotDraw));

        context.Target = target;
        context.BeginDraw();
        context.Clear(new Color4(0f, 0f, 0f, 0f));
        context.DrawImage(image, new Vector2(-left, -top), null, InterpolationMode.NearestNeighbor, CompositeMode.SourceCopy);
        context.EndDraw();
        context.Target = null;

        readback.CopyFromBitmap(target);
        var values = new float[width * height * Channels];
        var mapped = readback.Map(MapOptions.Read);
        try
        {
            for (var y = 0; y < height; y++)
                Marshal.Copy(mapped.Bits + y * (int)mapped.Pitch, values, y * width * Channels, width * Channels);
        }
        finally
        {
            readback.Unmap();
        }

        return new FloatRendering(left, top, width, height, values);
    }

    public Vector4 this[int x, int y]
    {
        get
        {
            var offset = ((y - Top) * Width + (x - Left)) * Channels;
            return new Vector4(pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
        }
    }

    public IEnumerable<(int X, int Y)> Coordinates()
    {
        for (var y = Top; y < Top + Height; y++)
        {
            for (var x = Left; x < Left + Width; x++)
                yield return (x, y);
        }
    }
}
