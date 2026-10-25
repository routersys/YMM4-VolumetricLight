using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace VolumetricLight.Harness;

internal sealed record HarnessOccluder(VolumetricLightOccluderEffect Effect, HarnessPose Pose, HarnessImage Image);

internal sealed class HarnessOccluderScene : IDisposable
{
    sealed class Entry(HarnessOccluder occluder, ID2D1Bitmap1 bitmap, AffineTransform2D placement, ID2D1Image centered, IVideoEffectProcessor blocker, IVideoEffectProcessor display) : IDisposable
    {
        public HarnessOccluder Occluder { get; } = occluder;

        public IVideoEffectProcessor Blocker { get; } = blocker;

        public IVideoEffectProcessor Display { get; } = display;

        public void Dispose()
        {
            Blocker.ClearInput();
            Display.ClearInput();
            Blocker.Dispose();
            Display.Dispose();
            placement.SetInput(0, null, true);
            centered.Dispose();
            placement.Dispose();
            bitmap.Dispose();
        }
    }

    readonly List<Entry> entries = [];

    public HarnessOccluderScene(IGraphicsDevicesAndContext context, IReadOnlyList<HarnessOccluder> occluders)
    {
        foreach (var occluder in occluders)
        {
            var bitmap = HarnessRenderer.CreateBitmap(context.DeviceContext, occluder.Image.Width, occluder.Image.Height, Vortice.Direct2D1.BitmapOptions.None);
            bitmap.CopyFromMemory(occluder.Image.Pixels, occluder.Image.Width * HarnessImage.BytesPerPixel);
            var placement = new AffineTransform2D(context.DeviceContext)
            {
                TransformMatrix = Matrix3x2.CreateTranslation(-occluder.Image.Width * 0.5f, -occluder.Image.Height * 0.5f),
            };
            placement.SetInput(0, bitmap, true);
            var centered = placement.Output;
            var blocker = occluder.Effect.CreateVideoEffect(context);
            blocker.SetInput(centered);
            var displayEffect = new VolumetricLightEffect();
            displayEffect.Intensity.Values[0].Value = 0d;
            var display = displayEffect.CreateVideoEffect(context);
            display.SetInput(centered);
            entries.Add(new Entry(occluder, bitmap, placement, centered, blocker, display));
        }
    }

    public IEnumerable<ID2D1Image> Layers => entries.Select(entry => entry.Display.Output);

    public void Update(Func<int, HarnessPose?, EffectDescription> describe, int frame)
    {
        foreach (var entry in entries)
        {
            var description = describe(frame, entry.Occluder.Pose);
            entry.Blocker.Update(description);
            entry.Display.Update(description);
        }
    }

    public void Dispose()
    {
        foreach (var entry in entries)
            entry.Dispose();
    }
}
