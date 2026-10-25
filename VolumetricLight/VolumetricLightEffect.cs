using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Effects;

namespace VolumetricLight;

[PluginDetails(AuthorName = "routersys")]
[VideoEffect(
    nameof(Texts.VolumetricLight),
    [VideoEffectCategories.Decoration],
    [nameof(Texts.TagVolumetricLight), nameof(Texts.TagVolumeLight), nameof(Texts.TagGodRay), nameof(Texts.TagLightShaft), nameof(Texts.TagCrepuscularRay), nameof(Texts.TagTyndall)],
    IsAviUtlSupported = false,
    ResourceType = typeof(Texts))]
public sealed class VolumetricLightEffect : VideoEffectBase
{
    public override string Label => Texts.VolumetricLight;

    public VolumetricLightEffect()
    {
        VolumetricLightTelemetry.EnsureStartedOnce();
        VolumetricLightUpdateNotifier.EnsureCheckedOnce();
    }

    [Display(GroupName = nameof(Texts.DirectionGroup), Name = nameof(Texts.Direction), Description = nameof(Texts.DirectionDescription), Order = 0, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "°", -180, 180)]
    public Animation Direction { get; } = new Animation(-90, -360, 360);

    [Display(GroupName = nameof(Texts.DirectionGroup), Name = nameof(Texts.Elevation), Description = nameof(Texts.ElevationDescription), Order = 1, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "°", -VolumetricLightSettings.MaximumElevationDegrees, VolumetricLightSettings.MaximumElevationDegrees)]
    public Animation Elevation { get; } = new Animation(55, -VolumetricLightSettings.MaximumElevationDegrees, VolumetricLightSettings.MaximumElevationDegrees);

    [Display(GroupName = nameof(Texts.DirectionGroup), Name = nameof(Texts.Spread), Description = nameof(Texts.SpreadDescription), Order = 2, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "°", -60, 60)]
    public Animation Spread { get; } = new Animation(10, -VolumetricLightSettings.MaximumSpreadDegrees, VolumetricLightSettings.MaximumSpreadDegrees);

    [Display(GroupName = nameof(Texts.DirectionGroup), Name = nameof(Texts.Length), Description = nameof(Texts.LengthDescription), Order = 3, ResourceType = typeof(Texts))]
    [AnimationSlider("F0", "px", 0, 4000)]
    public Animation Length { get; } = new Animation(1200, 0, VolumetricLightSettings.MaximumLength);

    [Display(GroupName = nameof(Texts.LightGroup), Name = nameof(Texts.Intensity), Description = nameof(Texts.IntensityDescription), Order = 10, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 300)]
    public Animation Intensity { get; } = new Animation(100, 0, VolumetricLightSettings.MaximumIntensityPercent);

    [Display(GroupName = nameof(Texts.LightGroup), Name = nameof(Texts.Threshold), Description = nameof(Texts.ThresholdDescription), Order = 11, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Threshold { get; } = new Animation(8, 0, 100);

    [Display(GroupName = nameof(Texts.LightGroup), Name = nameof(Texts.Tint), Description = nameof(Texts.TintDescription), Order = 12, ResourceType = typeof(Texts))]
    [ColorPicker]
    public Color Tint { get => _tint; set => Set(ref _tint, value); }
    private Color _tint = Colors.White;

    [Display(GroupName = nameof(Texts.BeamGroup), Name = nameof(Texts.Falloff), Description = nameof(Texts.FalloffDescription), Order = 20, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Falloff { get; } = new Animation(60, 0, 100);

    [Display(GroupName = nameof(Texts.BeamGroup), Name = nameof(Texts.LampSize), Description = nameof(Texts.LampSizeDescription), Order = 21, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "°", 0, 10)]
    public Animation LampSize { get; } = new Animation(1.5, 0, VolumetricLightSettings.MaximumLampRadiusDegrees);

    [Display(GroupName = nameof(Texts.BeamGroup), Name = nameof(Texts.Scattering), Description = nameof(Texts.ScatteringDescription), Order = 22, ResourceType = typeof(Texts))]
    [AnimationSlider("F0", "%", -90, 90)]
    public Animation Scattering { get; } = new Animation(30, -VolumetricLightSettings.MaximumAnisotropyPercent, VolumetricLightSettings.MaximumAnisotropyPercent);

    [Display(GroupName = nameof(Texts.OutputGroup), Name = nameof(Texts.ShowSource), Description = nameof(Texts.ShowSourceDescription), Order = 40, ResourceType = typeof(Texts))]
    [ToggleSlider]
    public bool ShowSource { get => _showSource; set => Set(ref _showSource, value); }
    private bool _showSource = true;

    [Display(GroupName = nameof(Texts.DustGroup), Name = nameof(Texts.DustAmount), Description = nameof(Texts.DustAmountDescription), Order = 30, ResourceType = typeof(Texts))]
    [AnimationSlider("F0", "%", 0, 100)]
    public Animation DustAmount { get; } = new Animation(0, 0, 100);

    [Display(GroupName = nameof(Texts.DustGroup), Name = nameof(Texts.DustSize), Description = nameof(Texts.DustSizeDescription), Order = 31, ResourceType = typeof(Texts))]
    [AnimationSlider("F0", "px", 20, 1000)]
    public Animation DustSize { get; } = new Animation(220, VolumetricLightSettings.MinimumDustSize, VolumetricLightSettings.MaximumDustSize);

    [Display(GroupName = nameof(Texts.DustGroup), Name = nameof(Texts.DustSpeed), Description = nameof(Texts.DustSpeedDescription), Order = 32, ResourceType = typeof(Texts))]
    [AnimationSlider("F0", "px/s", -500, 500)]
    public Animation DustSpeed { get; } = new Animation(40, -VolumetricLightSettings.MaximumDustSpeed, VolumetricLightSettings.MaximumDustSpeed);

    [Display(GroupName = nameof(Texts.DustGroup), Name = nameof(Texts.DustDirection), Description = nameof(Texts.DustDirectionDescription), Order = 33, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "°", -180, 180)]
    public Animation DustDirection { get; } = new Animation(-90, -360, 360);

    [Display(GroupName = nameof(Texts.DustGroup), Name = nameof(Texts.Seed), Description = nameof(Texts.SeedDescription), Order = 34, ResourceType = typeof(Texts))]
    [TextBoxSlider("F0", "", -VolumetricLightSettings.MaximumSeed, VolumetricLightSettings.MaximumSeed)]
    [Range(-VolumetricLightSettings.MaximumSeed, VolumetricLightSettings.MaximumSeed)]
    public int Seed
    {
        get => _seed;
        set => Set(ref _seed, Math.Clamp(value, -VolumetricLightSettings.MaximumSeed, VolumetricLightSettings.MaximumSeed));
    }
    private int _seed;

    [Display(GroupName = nameof(Texts.OutputGroup), Name = nameof(Texts.Quality), Description = nameof(Texts.QualityDescription), Order = 41, ResourceType = typeof(Texts))]
    [EnumComboBox]
    public VolumetricLightQuality Quality { get => _quality; set => Set(ref _quality, value); }
    private VolumetricLightQuality _quality = VolumetricLightQuality.Standard;

    private IAnimatable[]? _animatables;

    public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];

    public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
    {
        try
        {
            return new VolumetricLightEffectProcessor(devices, this);
        }
        catch (Exception exception)
        {
            VolumetricLightTelemetry.Report(exception);
            throw;
        }
    }

    protected override IEnumerable<IAnimatable> GetAnimatables()
        => _animatables ??= [Direction, Elevation, Spread, Length, Intensity, Threshold, Falloff, LampSize, Scattering, DustAmount, DustSize, DustSpeed, DustDirection];
}
