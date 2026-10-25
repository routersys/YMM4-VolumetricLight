using System.Drawing;
using System.Numerics;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Player.Video;

namespace VolumetricLight.Tests;

internal static class EffectDescriptions
{
    public const int Fps = 30;
    public const int ScreenWidth = 1920;
    public const int ScreenHeight = 1080;

    static readonly Size ScreenSize = new(ScreenWidth, ScreenHeight);

    public static DrawDescription Flat { get; } = new(Vector3.Zero, Vector2.Zero, Vector2.One, Vector3.Zero, Matrix4x4.Identity, InterpolationMode.Linear, 1d, false, []);

    public static EffectDescription At(int frame, int length) => At(frame, length, Flat);

    public static EffectDescription At(int frame, int length, DrawDescription draw) => At(frame, length, draw, ScreenSize);

    public static EffectDescription At(int frame, int length, DrawDescription draw, Size screenSize)
    {
        var timeline = new TimelineSourceDescription(
            screenSize,
            new FrameTime(frame, Fps),
            new FrameTime(length, Fps),
            Fps,
            TimelineSourceUsage.Playing,
            Guid.Empty,
            []);
        var item = new TimelineItemSourceDescription(timeline, frame, length, 0);
        return new EffectDescription(item, draw, 0, 1, 0, 1);
    }
}
