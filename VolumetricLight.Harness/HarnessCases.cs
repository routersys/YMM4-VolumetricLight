using System.Globalization;
using System.Numerics;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Json;

namespace VolumetricLight.Harness;

internal static class HarnessCases
{
    static readonly HarnessPose RotatedY = HarnessPose.Flat with { Rotation = new Vector3(0f, 35f, 0f) };
    static readonly HarnessPose RotatedX = HarnessPose.Flat with { Rotation = new Vector3(25f, 0f, 0f) };
    static readonly HarnessPose RotatedZ = HarnessPose.Flat with { Rotation = new Vector3(0f, 0f, 20f) };
    static readonly HarnessPose Shrunk = HarnessPose.Flat with { Zoom = new Vector2(0.6f, 0.6f) };
    static readonly HarnessPose Offset = HarnessPose.Flat with { Draw = new Vector3(-180f, 60f, 0f) };
    static readonly HarnessPose Near = HarnessPose.Flat with { Draw = new Vector3(0f, 0f, 300f) };
    static readonly HarnessPose Mirrored = HarnessPose.Flat with { Invert = true };
    static readonly HarnessPose Orbit = HarnessPose.Flat with { Camera = HarnessPose.CameraAt(new Vector3(220f, 0f, 300f), new Vector3(0f, -15f, 0f)) };
    static readonly HarnessPose Telephoto = HarnessPose.Flat with { PerspectiveDistance = 3000f };

    public static IEnumerable<(string Name, VolumetricLightEffect Effect, IReadOnlyList<int> Frames, HarnessPose Pose)> All()
    {
        yield return ("default", Create(), [0], HarnessPose.Flat);
        yield return ("direction-animated-frames", Create(effect => effect.Direction.CopyFrom(Linear(-150d, -30d))), [0, 100, 200, 299], HarnessPose.Flat);
        yield return ("direction-0", Create(effect => effect.Direction.Values[0].Value = 0), [0], HarnessPose.Flat);
        yield return ("direction-180", Create(effect => effect.Direction.Values[0].Value = 180), [0], HarnessPose.Flat);
        yield return ("elevation-15", Create(effect => effect.Elevation.Values[0].Value = 15), [0], HarnessPose.Flat);
        yield return ("elevation-70", Create(effect => effect.Elevation.Values[0].Value = 70), [0], HarnessPose.Flat);
        yield return ("elevation-minus-35", Create(effect => effect.Elevation.Values[0].Value = -35), [0], HarnessPose.Flat);
        yield return ("spread-0", Create(effect => effect.Spread.Values[0].Value = 0), [0], HarnessPose.Flat);
        yield return ("spread-35", Create(effect => effect.Spread.Values[0].Value = 35), [0], HarnessPose.Flat);
        yield return ("spread-minus-25", Create(effect => effect.Spread.Values[0].Value = -25), [0], HarnessPose.Flat);
        yield return ("length-400", Create(effect => effect.Length.Values[0].Value = 400), [0], HarnessPose.Flat);
        yield return ("length-2800", Create(effect => effect.Length.Values[0].Value = 2800), [0], HarnessPose.Flat);
        yield return ("length-0", Create(effect => effect.Length.Values[0].Value = 0), [0], HarnessPose.Flat);
        yield return ("intensity-40", Create(effect => effect.Intensity.Values[0].Value = 40), [0], HarnessPose.Flat);
        yield return ("intensity-250", Create(effect => effect.Intensity.Values[0].Value = 250), [0], HarnessPose.Flat);
        yield return ("threshold-0", Create(effect => effect.Threshold.Values[0].Value = 0), [0], HarnessPose.Flat);
        yield return ("threshold-60", Create(effect => effect.Threshold.Values[0].Value = 60), [0], HarnessPose.Flat);
        yield return ("tint-amber", Create(effect => effect.Tint = System.Windows.Media.Color.FromRgb(255, 190, 90)), [0], HarnessPose.Flat);
        yield return ("falloff-0", Create(effect => effect.Falloff.Values[0].Value = 0), [0], HarnessPose.Flat);
        yield return ("falloff-95", Create(effect => effect.Falloff.Values[0].Value = 95), [0], HarnessPose.Flat);
        yield return ("lamp-size-0", Create(effect => effect.LampSize.Values[0].Value = 0), [0], HarnessPose.Flat);
        yield return ("lamp-size-6", Create(effect => effect.LampSize.Values[0].Value = 6), [0], HarnessPose.Flat);
        yield return ("scattering-minus-60", Create(effect => effect.Scattering.Values[0].Value = -60), [0], HarnessPose.Flat);
        yield return ("scattering-85", Create(effect => effect.Scattering.Values[0].Value = 85), [0], HarnessPose.Flat);
        yield return ("source-hidden", Create(effect => effect.ShowSource = false), [0], HarnessPose.Flat);
        yield return ("dust-60", Create(effect => effect.DustAmount.Values[0].Value = 60), [0], HarnessPose.Flat);
        yield return ("dust-60-animated-frames", Create(effect => effect.DustAmount.Values[0].Value = 60), [0, 90, 180], HarnessPose.Flat);
        yield return ("dust-seed-7", Create(effect => { effect.DustAmount.Values[0].Value = 60; effect.Seed = 7; }), [0], HarnessPose.Flat);
        yield return ("quality-high", Create(effect => effect.Quality = VolumetricLightQuality.High), [0], HarnessPose.Flat);
        yield return ("quality-ultra", Create(effect => effect.Quality = VolumetricLightQuality.Ultra), [0], HarnessPose.Flat);
        yield return ("pose-rotated-y", Create(), [0], RotatedY);
        yield return ("pose-rotated-x", Create(), [0], RotatedX);
        yield return ("pose-rotated-z", Create(), [0], RotatedZ);
        yield return ("pose-shrunk", Create(), [0], Shrunk);
        yield return ("pose-offset", Create(), [0], Offset);
        yield return ("pose-near", Create(), [0], Near);
        yield return ("pose-mirrored", Create(), [0], Mirrored);
        yield return ("pose-orbit", Create(), [0], Orbit);
        yield return ("pose-telephoto", Create(), [0], Telephoto);
        yield return ("pose-rotated-y-converging", Create(effect => effect.Spread.Values[0].Value = -25), [0], RotatedY);
    }

    public static IEnumerable<(string Name, Func<VolumetricLightEffect> Create, Action<VolumetricLightEffect> Change, int Frame, HarnessPose Pose)> Transitions()
    {
        yield return ("direction-minus-90-to-0", () => Create(), effect => effect.Direction.Values[0].Value = 0, 0, HarnessPose.Flat);
        yield return ("elevation-35-to-60", () => Create(), effect => effect.Elevation.Values[0].Value = 60, 0, HarnessPose.Flat);
        yield return ("spread-12-to-minus-20", () => Create(), effect => effect.Spread.Values[0].Value = -20, 0, HarnessPose.Flat);
        yield return ("length-1200-to-500", () => Create(), effect => effect.Length.Values[0].Value = 500, 0, HarnessPose.Flat);
        yield return ("intensity-100-to-30", () => Create(), effect => effect.Intensity.Values[0].Value = 30, 0, HarnessPose.Flat);
        yield return ("threshold-25-to-70", () => Create(), effect => effect.Threshold.Values[0].Value = 70, 0, HarnessPose.Flat);
        yield return ("threshold-70-to-10", () => Create(effect => effect.Threshold.Values[0].Value = 70), effect => effect.Threshold.Values[0].Value = 10, 0, HarnessPose.Flat);
        yield return ("tint-white-to-blue", () => Create(), effect => effect.Tint = System.Windows.Media.Color.FromRgb(90, 140, 255), 0, HarnessPose.Flat);
        yield return ("show-source-on-to-off", () => Create(), effect => effect.ShowSource = false, 0, HarnessPose.Flat);
        yield return ("show-source-off-to-on", () => Create(effect => effect.ShowSource = false), effect => effect.ShowSource = true, 0, HarnessPose.Flat);
        yield return ("dust-0-to-60", () => Create(), effect => effect.DustAmount.Values[0].Value = 60, 90, HarnessPose.Flat);
        yield return ("quality-standard-to-ultra", () => Create(), effect => effect.Quality = VolumetricLightQuality.Ultra, 0, HarnessPose.Flat);
        yield return ("falloff-60-to-0", () => Create(), effect => effect.Falloff.Values[0].Value = 0, 0, HarnessPose.Flat);
        yield return ("scattering-30-to-minus-50", () => Create(), effect => effect.Scattering.Values[0].Value = -50, 0, HarnessPose.Flat);
        yield return ("lamp-size-1.5-to-6", () => Create(), effect => effect.LampSize.Values[0].Value = 6, 0, HarnessPose.Flat);
        yield return ("rotated-y-length-1200-to-500", () => Create(), effect => effect.Length.Values[0].Value = 500, 0, RotatedY);
    }

    public static IEnumerable<(string Name, VolumetricLightEffect Effect, HarnessPose Pose)> Benchmarks()
    {
        yield return ("default", Create(), HarnessPose.Flat);
        yield return ("quality-ultra", Create(effect => effect.Quality = VolumetricLightQuality.Ultra), HarnessPose.Flat);
        yield return ("dust-60", Create(effect => effect.DustAmount.Values[0].Value = 60), HarnessPose.Flat);
        yield return ("pose-rotated-y", Create(), RotatedY);
        yield return ("direction-animated", Create(effect => effect.Direction.CopyFrom(Linear(-150d, -30d))), HarnessPose.Flat);
    }

    static Animation Linear(double from, double to)
        => Json.LoadFromText<Animation>(string.Create(CultureInfo.InvariantCulture, $$"""{"AnimationType":"直線移動","Values":[{"Value":{{from}}},{"Value":{{to}}}]}""")) ?? throw new HarnessException("アニメーションを読み込めません。");

    public static VolumetricLightEffect Create(Action<VolumetricLightEffect>? configure = null)
    {
        var effect = new VolumetricLightEffect();
        configure?.Invoke(effect);
        return effect;
    }
}
