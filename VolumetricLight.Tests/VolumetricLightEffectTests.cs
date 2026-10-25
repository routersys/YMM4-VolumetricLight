using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Json;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project;

namespace VolumetricLight.Tests;

public sealed class VolumetricLightEffectTests
{
    static PropertyInfo Property(string name) => typeof(VolumetricLightEffect).GetProperty(name)!;

    static T Attribute<T>(string property) where T : Attribute => Property(property).GetCustomAttribute<T>()!;

    static Animation[] Animations(VolumetricLightEffect effect)
        => [effect.Direction, effect.Elevation, effect.Spread, effect.Length, effect.Intensity, effect.Threshold, effect.Falloff, effect.LampSize, effect.Scattering, effect.DustAmount, effect.DustSize, effect.DustSpeed, effect.DustDirection];

    [Theory]
    [InlineData(nameof(VolumetricLightEffect.Direction), -90d, -360d, 360d)]
    [InlineData(nameof(VolumetricLightEffect.Elevation), 55d, -89d, 89d)]
    [InlineData(nameof(VolumetricLightEffect.Spread), 10d, -85d, 85d)]
    [InlineData(nameof(VolumetricLightEffect.Length), 1200d, 0d, 100000d)]
    [InlineData(nameof(VolumetricLightEffect.Intensity), 100d, 0d, 1000d)]
    [InlineData(nameof(VolumetricLightEffect.Threshold), 8d, 0d, 100d)]
    [InlineData(nameof(VolumetricLightEffect.Falloff), 60d, 0d, 100d)]
    [InlineData(nameof(VolumetricLightEffect.LampSize), 1.5d, 0d, 30d)]
    [InlineData(nameof(VolumetricLightEffect.Scattering), 30d, -95d, 95d)]
    [InlineData(nameof(VolumetricLightEffect.DustAmount), 0d, 0d, 100d)]
    [InlineData(nameof(VolumetricLightEffect.DustSize), 220d, 8d, 4000d)]
    [InlineData(nameof(VolumetricLightEffect.DustSpeed), 40d, -5000d, 5000d)]
    [InlineData(nameof(VolumetricLightEffect.DustDirection), -90d, -360d, 360d)]
    public void AnimatedParametersStartFromTheirDefaultsWithinTheirRange(string name, double defaultValue, double minimum, double maximum)
    {
        var effect = new VolumetricLightEffect();

        var animation = (Animation)Property(name).GetValue(effect)!;

        Assert.Equal(defaultValue, animation.DefaultValue);
        Assert.Equal(minimum, animation.MinValue);
        Assert.Equal(maximum, animation.MaxValue);
        Assert.Equal(defaultValue, animation.GetValue(0, 1, EffectDescriptions.Fps));
    }

    [Fact]
    public void TheRangesMatchTheLimitsTheModelCovers()
    {
        var effect = new VolumetricLightEffect();

        Assert.Equal(-VolumetricLightSettings.MaximumElevationDegrees, effect.Elevation.MinValue);
        Assert.Equal(VolumetricLightSettings.MaximumElevationDegrees, effect.Elevation.MaxValue);
        Assert.Equal(-VolumetricLightSettings.MaximumSpreadDegrees, effect.Spread.MinValue);
        Assert.Equal(VolumetricLightSettings.MaximumSpreadDegrees, effect.Spread.MaxValue);
        Assert.Equal(VolumetricLightSettings.MaximumLength, effect.Length.MaxValue);
        Assert.Equal(VolumetricLightSettings.MaximumIntensityPercent, effect.Intensity.MaxValue);
        Assert.Equal(VolumetricLightSettings.MaximumLampRadiusDegrees, effect.LampSize.MaxValue);
        Assert.Equal(-VolumetricLightSettings.MaximumAnisotropyPercent, effect.Scattering.MinValue);
        Assert.Equal(VolumetricLightSettings.MaximumAnisotropyPercent, effect.Scattering.MaxValue);
        Assert.Equal(VolumetricLightSettings.MinimumDustSize, effect.DustSize.MinValue);
        Assert.Equal(VolumetricLightSettings.MaximumDustSize, effect.DustSize.MaxValue);
        Assert.Equal(-VolumetricLightSettings.MaximumDustSpeed, effect.DustSpeed.MinValue);
        Assert.Equal(VolumetricLightSettings.MaximumDustSpeed, effect.DustSpeed.MaxValue);
        Assert.Equal(VolumetricLightSettings.PercentScale, effect.Falloff.MaxValue);
        Assert.Equal(VolumetricLightSettings.PercentScale, effect.Threshold.MaxValue);
        Assert.Equal(VolumetricLightSettings.PercentScale, effect.DustAmount.MaxValue);
    }

    [Fact]
    public void TheDefaultsKeepTheSourceVisibleAtStandardQualityWithTheDustOff()
    {
        var effect = new VolumetricLightEffect();

        Assert.True(effect.ShowSource);
        Assert.Equal(VolumetricLightQuality.Standard, effect.Quality);
        Assert.Equal(Colors.White, effect.Tint);
        Assert.Equal(0, effect.Seed);
        Assert.Equal(0, effect.OcclusionChannel);
    }

    [Theory]
    [InlineData(-3, 0)]
    [InlineData(0, 0)]
    [InlineData(5, 5)]
    [InlineData(99, VolumetricLightSettings.MaximumOcclusionChannel)]
    public void TheOcclusionChannelIsClampedToItsRange(int value, int expected)
    {
        var effect = new VolumetricLightEffect { OcclusionChannel = 4 };

        effect.OcclusionChannel = value;

        Assert.Equal(expected, effect.OcclusionChannel);
    }

    [Theory]
    [InlineData(-5000, -VolumetricLightSettings.MaximumSeed)]
    [InlineData(5000, VolumetricLightSettings.MaximumSeed)]
    [InlineData(-3, -3)]
    [InlineData(42, 42)]
    public void TheSeedIsClampedToItsRange(int value, int expected)
    {
        var effect = new VolumetricLightEffect { Seed = value };

        Assert.Equal(expected, effect.Seed);
    }

    [Fact]
    public void TheAnimatedParametersAreListedOnceAndReused()
    {
        var effect = new VolumetricLightEffect();
        var method = typeof(VolumetricLightEffect).GetMethod("GetAnimatables", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var first = (IEnumerable<IAnimatable>)method.Invoke(effect, null)!;
        var second = (IEnumerable<IAnimatable>)method.Invoke(effect, null)!;

        Assert.Same(first, second);
        Assert.Equal(Animations(effect), first);
    }

    [Fact]
    public void TheLabelIsTheLocalizedEffectName()
    {
        Assert.Equal(Texts.VolumetricLight, new VolumetricLightEffect().Label);
    }

    [Fact]
    public void EveryAnimatedParameterReceivesTheAnimationParameters()
    {
        var effect = new VolumetricLightEffect();

        effect.SetAnimationParameters(120, EffectDescriptions.Fps);

        Assert.Equal(13, Animations(effect).Length);
        Assert.All(Animations(effect), animation => Assert.Equal(120, animation.Length));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void NoExoFilterIsWrittenForAviUtl(int keyFrameIndex)
    {
        var effect = new VolumetricLightEffect();

        var description = new ExoOutputDescription(new VideoInfo(), string.Empty, new AviUtlDirectories(string.Empty, string.Empty));

        Assert.Empty(effect.CreateExoVideoFilters(keyFrameIndex, description));
    }

    [Fact]
    public void TheAuthorIsDeclared()
    {
        var details = typeof(VolumetricLightEffect).GetCustomAttribute<PluginDetailsAttribute>()!;

        Assert.Equal("routersys", details.AuthorName);
    }

    [Fact]
    public void TheEffectIsRegisteredForDecorationWithoutAviUtlSupport()
    {
        var attribute = typeof(VolumetricLightEffect).GetCustomAttribute<VideoEffectAttribute>()!;

        Assert.Equal(nameof(Texts.VolumetricLight), attribute.Name);
        Assert.Equal([VideoEffectCategories.Decoration], attribute.Categories);
        Assert.Equal(
            [nameof(Texts.TagVolumetricLight), nameof(Texts.TagVolumeLight), nameof(Texts.TagGodRay), nameof(Texts.TagLightShaft), nameof(Texts.TagCrepuscularRay), nameof(Texts.TagTyndall)],
            attribute.Keywords);
        Assert.False(attribute.IsAviUtlSupported);
        Assert.True(attribute.IsEffectItemSupported);
        Assert.Equal(typeof(Texts), attribute.ResourceType);
        Assert.Equal(Texts.VolumetricLight, attribute.GetName());
    }

    [Theory]
    [InlineData(nameof(VolumetricLightEffect.Direction), nameof(Texts.DirectionGroup), nameof(Texts.Direction), nameof(Texts.DirectionDescription), 0)]
    [InlineData(nameof(VolumetricLightEffect.Elevation), nameof(Texts.DirectionGroup), nameof(Texts.Elevation), nameof(Texts.ElevationDescription), 1)]
    [InlineData(nameof(VolumetricLightEffect.Spread), nameof(Texts.DirectionGroup), nameof(Texts.Spread), nameof(Texts.SpreadDescription), 2)]
    [InlineData(nameof(VolumetricLightEffect.Length), nameof(Texts.DirectionGroup), nameof(Texts.Length), nameof(Texts.LengthDescription), 3)]
    [InlineData(nameof(VolumetricLightEffect.Intensity), nameof(Texts.LightGroup), nameof(Texts.Intensity), nameof(Texts.IntensityDescription), 10)]
    [InlineData(nameof(VolumetricLightEffect.Threshold), nameof(Texts.LightGroup), nameof(Texts.Threshold), nameof(Texts.ThresholdDescription), 11)]
    [InlineData(nameof(VolumetricLightEffect.Tint), nameof(Texts.LightGroup), nameof(Texts.Tint), nameof(Texts.TintDescription), 12)]
    [InlineData(nameof(VolumetricLightEffect.Falloff), nameof(Texts.BeamGroup), nameof(Texts.Falloff), nameof(Texts.FalloffDescription), 20)]
    [InlineData(nameof(VolumetricLightEffect.LampSize), nameof(Texts.BeamGroup), nameof(Texts.LampSize), nameof(Texts.LampSizeDescription), 21)]
    [InlineData(nameof(VolumetricLightEffect.Scattering), nameof(Texts.BeamGroup), nameof(Texts.Scattering), nameof(Texts.ScatteringDescription), 22)]
    [InlineData(nameof(VolumetricLightEffect.DustAmount), nameof(Texts.DustGroup), nameof(Texts.DustAmount), nameof(Texts.DustAmountDescription), 30)]
    [InlineData(nameof(VolumetricLightEffect.DustSize), nameof(Texts.DustGroup), nameof(Texts.DustSize), nameof(Texts.DustSizeDescription), 31)]
    [InlineData(nameof(VolumetricLightEffect.DustSpeed), nameof(Texts.DustGroup), nameof(Texts.DustSpeed), nameof(Texts.DustSpeedDescription), 32)]
    [InlineData(nameof(VolumetricLightEffect.DustDirection), nameof(Texts.DustGroup), nameof(Texts.DustDirection), nameof(Texts.DustDirectionDescription), 33)]
    [InlineData(nameof(VolumetricLightEffect.Seed), nameof(Texts.DustGroup), nameof(Texts.Seed), nameof(Texts.SeedDescription), 34)]
    [InlineData(nameof(VolumetricLightEffect.OcclusionChannel), nameof(Texts.OcclusionGroup), nameof(Texts.OcclusionChannel), nameof(Texts.OcclusionChannelDescription), 50)]
    [InlineData(nameof(VolumetricLightEffect.ShowSource), nameof(Texts.OutputGroup), nameof(Texts.ShowSource), nameof(Texts.ShowSourceDescription), 40)]
    [InlineData(nameof(VolumetricLightEffect.Quality), nameof(Texts.OutputGroup), nameof(Texts.Quality), nameof(Texts.QualityDescription), 41)]
    public void EveryParameterIsDisplayedInItsGroupInOrder(string property, string group, string name, string description, int order)
    {
        var display = Attribute<DisplayAttribute>(property);

        Assert.Equal(group, display.GroupName);
        Assert.Equal(name, display.Name);
        Assert.Equal(description, display.Description);
        Assert.Equal(order, display.Order);
        Assert.Equal(typeof(Texts), display.ResourceType);
    }

    [Fact]
    public void NoTwoParametersShareAnOrder()
    {
        var orders = typeof(VolumetricLightEffect).GetProperties()
            .Select(property => property.GetCustomAttribute<DisplayAttribute>())
            .OfType<DisplayAttribute>()
            .Select(display => display.Order)
            .ToArray();

        Assert.Equal(18, orders.Length);
        Assert.Equal(orders.Length, orders.Distinct().Count());
    }

    [Theory]
    [InlineData(nameof(VolumetricLightEffect.Direction), "F1", "°", -180d, 180d)]
    [InlineData(nameof(VolumetricLightEffect.Elevation), "F1", "°", -89d, 89d)]
    [InlineData(nameof(VolumetricLightEffect.Spread), "F1", "°", -60d, 60d)]
    [InlineData(nameof(VolumetricLightEffect.Length), "F0", "px", 0d, 4000d)]
    [InlineData(nameof(VolumetricLightEffect.Intensity), "F1", "%", 0d, 300d)]
    [InlineData(nameof(VolumetricLightEffect.Threshold), "F1", "%", 0d, 100d)]
    [InlineData(nameof(VolumetricLightEffect.Falloff), "F1", "%", 0d, 100d)]
    [InlineData(nameof(VolumetricLightEffect.LampSize), "F1", "°", 0d, 10d)]
    [InlineData(nameof(VolumetricLightEffect.Scattering), "F0", "%", -90d, 90d)]
    [InlineData(nameof(VolumetricLightEffect.DustAmount), "F0", "%", 0d, 100d)]
    [InlineData(nameof(VolumetricLightEffect.DustSize), "F0", "px", 20d, 1000d)]
    [InlineData(nameof(VolumetricLightEffect.DustSpeed), "F0", "px/s", -500d, 500d)]
    [InlineData(nameof(VolumetricLightEffect.DustDirection), "F1", "°", -180d, 180d)]
    public void AnimatedParametersAreEditedWithAnimationSliders(string property, string format, string unit, double minimum, double maximum)
    {
        var slider = Attribute<AnimationSliderAttribute>(property);

        Assert.Equal(format, slider.StringFormat);
        Assert.Equal(unit, slider.UnitText);
        Assert.Equal(minimum, slider.DefaultMin);
        Assert.Equal(maximum, slider.DefaultMax);
    }

    [Fact]
    public void TheSeedIsEditedWithATextBoxAndTheQualityWithAComboBox()
    {
        Assert.NotNull(Attribute<TextBoxSliderAttribute>(nameof(VolumetricLightEffect.Seed)));
        Assert.NotNull(Attribute<EnumComboBoxAttribute>(nameof(VolumetricLightEffect.Quality)));
        Assert.NotNull(Attribute<ColorPickerAttribute>(nameof(VolumetricLightEffect.Tint)));
        Assert.NotNull(Attribute<ToggleSliderAttribute>(nameof(VolumetricLightEffect.ShowSource)));
        Assert.NotNull(Attribute<TextBoxSliderAttribute>(nameof(VolumetricLightEffect.OcclusionChannel)));
    }

    [Fact]
    public void EverySettingSurvivesAProjectRoundTrip()
    {
        var effect = new VolumetricLightEffect
        {
            Tint = Color.FromRgb(255, 140, 40),
            ShowSource = false,
            Seed = -321,
            OcclusionChannel = 4,
            Quality = VolumetricLightQuality.Ultra,
        };
        var values = new[] { 135d, 40d, -22.5d, 800d, 180d, 20d, 45d, 4d, -35d, 65d, 310d, -120d, 15d };
        foreach (var (animation, value) in Animations(effect).Zip(values))
            animation.Values[0].Value = value;

        var clone = Json.GetClone(effect)!;

        Assert.NotSame(effect, clone);
        Assert.Equal(Color.FromRgb(255, 140, 40), clone.Tint);
        Assert.False(clone.ShowSource);
        Assert.Equal(-321, clone.Seed);
        Assert.Equal(4, clone.OcclusionChannel);
        Assert.Equal(VolumetricLightQuality.Ultra, clone.Quality);
        Assert.Equal(values, Animations(clone).Select(animation => animation.GetValue(0, 1, EffectDescriptions.Fps)));
    }
}
