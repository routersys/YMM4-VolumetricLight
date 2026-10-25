using System.ComponentModel.DataAnnotations;
using System.Reflection;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Json;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project;

namespace VolumetricLight.Tests;

public sealed class VolumetricLightOccluderEffectTests
{
    static PropertyInfo Property(string name) => typeof(VolumetricLightOccluderEffect).GetProperty(name)!;

    static T Attribute<T>(string property) where T : Attribute => Property(property).GetCustomAttribute<T>()!;

    [Fact]
    public void TheChannelStartsAtOneAndStaysWithinTheNumbersTheLightAccepts()
    {
        var effect = new VolumetricLightOccluderEffect();

        Assert.Equal(1, effect.Channel);
        effect.Channel = 5;
        effect.Channel = -4;
        Assert.Equal(1, effect.Channel);
        effect.Channel = 5;
        effect.Channel = 0;
        Assert.Equal(1, effect.Channel);
        effect.Channel = 99;
        Assert.Equal(VolumetricLightSettings.MaximumOcclusionChannel, effect.Channel);
        effect.Channel = 5;
        Assert.Equal(5, effect.Channel);
    }

    [Fact]
    public void TheStrengthStartsFullAndStaysWithinPercent()
    {
        var effect = new VolumetricLightOccluderEffect();

        Assert.Equal(100d, effect.Strength.DefaultValue);
        Assert.Equal(0d, effect.Strength.MinValue);
        Assert.Equal(VolumetricLightSettings.MaximumOccluderStrengthPercent, effect.Strength.MaxValue);
        Assert.Equal(100d, effect.Strength.GetValue(0, 1, EffectDescriptions.Fps));
    }

    [Fact]
    public void TheStrengthIsTheOnlyAnimatedParameterAndIsListedOnce()
    {
        var effect = new VolumetricLightOccluderEffect();
        var method = typeof(VolumetricLightOccluderEffect).GetMethod("GetAnimatables", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var first = (IEnumerable<IAnimatable>)method.Invoke(effect, null)!;
        var second = (IEnumerable<IAnimatable>)method.Invoke(effect, null)!;

        Assert.Same(first, second);
        Assert.Equal([effect.Strength], first);
        effect.SetAnimationParameters(90, EffectDescriptions.Fps);
        Assert.Equal(90, effect.Strength.Length);
    }

    [Fact]
    public void TheLabelIsTheLocalizedEffectName()
    {
        Assert.Equal(Texts.VolumetricLightOccluder, new VolumetricLightOccluderEffect().Label);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public void NoExoFilterIsWrittenForAviUtl(int keyFrameIndex)
    {
        var effect = new VolumetricLightOccluderEffect();

        var description = new ExoOutputDescription(new VideoInfo(), string.Empty, new AviUtlDirectories(string.Empty, string.Empty));

        Assert.Empty(effect.CreateExoVideoFilters(keyFrameIndex, description));
    }

    [Fact]
    public void TheEffectIsRegisteredForDecorationWithoutAviUtlSupport()
    {
        var attribute = typeof(VolumetricLightOccluderEffect).GetCustomAttribute<VideoEffectAttribute>()!;

        Assert.Equal(nameof(Texts.VolumetricLightOccluder), attribute.Name);
        Assert.Equal([VideoEffectCategories.Decoration], attribute.Categories);
        Assert.Equal([nameof(Texts.TagVolumetricLight), nameof(Texts.TagGodRay), nameof(Texts.TagShadow), nameof(Texts.TagLightBlocking)], attribute.Keywords);
        Assert.False(attribute.IsAviUtlSupported);
        Assert.Equal(typeof(Texts), attribute.ResourceType);
        Assert.Equal("routersys", typeof(VolumetricLightOccluderEffect).GetCustomAttribute<PluginDetailsAttribute>()!.AuthorName);
    }

    [Theory]
    [InlineData(nameof(VolumetricLightOccluderEffect.Channel), nameof(Texts.OccluderChannel), nameof(Texts.OccluderChannelDescription), 0)]
    [InlineData(nameof(VolumetricLightOccluderEffect.Strength), nameof(Texts.OccluderStrength), nameof(Texts.OccluderStrengthDescription), 1)]
    public void EveryParameterIsDisplayedInTheBlockingGroupInOrder(string property, string name, string description, int order)
    {
        var display = Attribute<DisplayAttribute>(property);

        Assert.Equal(nameof(Texts.OcclusionGroup), display.GroupName);
        Assert.Equal(name, display.Name);
        Assert.Equal(description, display.Description);
        Assert.Equal(order, display.Order);
        Assert.Equal(typeof(Texts), display.ResourceType);
    }

    [Fact]
    public void TheChannelIsEditedWithATextBoxAndTheStrengthWithASlider()
    {
        var channel = Attribute<TextBoxSliderAttribute>(nameof(VolumetricLightOccluderEffect.Channel));
        var strength = Attribute<AnimationSliderAttribute>(nameof(VolumetricLightOccluderEffect.Strength));

        Assert.Equal("F0", channel.StringFormat);
        Assert.Equal((1d, (double)VolumetricLightSettings.MaximumOcclusionChannel), (channel.DefaultMin, channel.DefaultMax));
        Assert.Equal(("F1", "%", 0d, 100d), (strength.StringFormat, strength.UnitText, strength.DefaultMin, strength.DefaultMax));
    }

    [Fact]
    public void EverySettingSurvivesAProjectRoundTrip()
    {
        var effect = new VolumetricLightOccluderEffect { Channel = 3 };
        effect.Strength.Values[0].Value = 42.5d;

        var clone = Json.GetClone(effect)!;

        Assert.NotSame(effect, clone);
        Assert.Equal(3, clone.Channel);
        Assert.Equal(42.5d, clone.Strength.GetValue(0, 1, EffectDescriptions.Fps));
    }
}
