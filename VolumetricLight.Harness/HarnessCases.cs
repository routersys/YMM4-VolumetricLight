using YukkuriMovieMaker.Plugin.Effects;

namespace VolumetricLight.Harness;

internal static class HarnessCases
{
    public static IEnumerable<(string Name, IVideoEffect Effect, IReadOnlyList<int> Frames)> All()
    {
        yield break;
    }

    public static IEnumerable<(string Name, Func<IVideoEffect> Create, Action<IVideoEffect> Change, int Frame)> Transitions()
    {
        yield break;
    }

    public static IEnumerable<(string Name, IVideoEffect Effect)> Benchmarks()
    {
        yield break;
    }
}
