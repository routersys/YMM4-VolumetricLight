using System.ComponentModel.DataAnnotations;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Effects;

namespace VolumetricLight;

[PluginDetails(AuthorName = "routersys")]
[VideoEffect(
    nameof(Texts.VolumetricLightOccluder),
    [VideoEffectCategories.Decoration],
    [nameof(Texts.TagVolumetricLight), nameof(Texts.TagGodRay), nameof(Texts.TagShadow), nameof(Texts.TagLightBlocking)],
    IsAviUtlSupported = false,
    ResourceType = typeof(Texts))]
public sealed class VolumetricLightOccluderEffect : VideoEffectBase
{
    public override string Label => Texts.VolumetricLightOccluder;

    public VolumetricLightOccluderEffect()
    {
        VolumetricLightTelemetry.EnsureStartedOnce();
        VolumetricLightUpdateNotifier.EnsureCheckedOnce();
    }

    [Display(GroupName = nameof(Texts.OcclusionGroup), Name = nameof(Texts.OccluderChannel), Description = nameof(Texts.OccluderChannelDescription), Order = 0, ResourceType = typeof(Texts))]
    [TextBoxSlider("F0", "", 1, VolumetricLightSettings.MaximumOcclusionChannel)]
    [Range(1, VolumetricLightSettings.MaximumOcclusionChannel)]
    public int Channel
    {
        get => _channel;
        set => Set(ref _channel, Math.Clamp(value, 1, VolumetricLightSettings.MaximumOcclusionChannel));
    }
    private int _channel = 1;

    [Display(GroupName = nameof(Texts.OcclusionGroup), Name = nameof(Texts.OccluderStrength), Description = nameof(Texts.OccluderStrengthDescription), Order = 1, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Strength { get; } = new Animation(100, 0, VolumetricLightSettings.MaximumOccluderStrengthPercent);

    private IAnimatable[]? _animatables;

    public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];

    public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
        => new VolumetricLightOccluderEffectProcessor(devices, this);

    protected override IEnumerable<IAnimatable> GetAnimatables() => _animatables ??= [Strength];
}
