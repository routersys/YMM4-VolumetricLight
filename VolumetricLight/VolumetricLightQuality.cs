using System.ComponentModel.DataAnnotations;

namespace VolumetricLight;

public enum VolumetricLightQuality
{
    [Display(Name = nameof(Texts.QualityStandard), Description = nameof(Texts.QualityStandardDescription), ResourceType = typeof(Texts))]
    Standard = 1,

    [Display(Name = nameof(Texts.QualityHigh), Description = nameof(Texts.QualityHighDescription), ResourceType = typeof(Texts))]
    High = 2,

    [Display(Name = nameof(Texts.QualityUltra), Description = nameof(Texts.QualityUltraDescription), ResourceType = typeof(Texts))]
    Ultra = 3,
}
