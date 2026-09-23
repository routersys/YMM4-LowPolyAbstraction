using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Json;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project;

namespace LowPolyAbstraction.Tests;

public sealed class LowPolyAbstractionEffectTests
{
    static PropertyInfo Property(string name) => typeof(LowPolyAbstractionEffect).GetProperty(name)!;

    static T Attribute<T>(string property) where T : Attribute => Property(property).GetCustomAttribute<T>()!;

    static Animation[] Animations(LowPolyAbstractionEffect effect)
        => [effect.Amount, effect.Detail, effect.Fidelity, effect.Refine, effect.Gradient, effect.Wireframe, effect.Saturation, effect.Jitter];

    [Theory]
    [InlineData(nameof(LowPolyAbstractionEffect.Amount), 100d, 0d, 100d)]
    [InlineData(nameof(LowPolyAbstractionEffect.Detail), 60d, 0d, 100d)]
    [InlineData(nameof(LowPolyAbstractionEffect.Fidelity), 70d, 0d, 100d)]
    [InlineData(nameof(LowPolyAbstractionEffect.Refine), 50d, 0d, 100d)]
    [InlineData(nameof(LowPolyAbstractionEffect.Gradient), 30d, 0d, 100d)]
    [InlineData(nameof(LowPolyAbstractionEffect.Wireframe), 0d, 0d, 100d)]
    [InlineData(nameof(LowPolyAbstractionEffect.Saturation), 30d, 0d, 100d)]
    [InlineData(nameof(LowPolyAbstractionEffect.Jitter), 25d, 0d, 100d)]
    public void AnimatedParametersStartFromTheirDefaultsWithinTheirRange(string name, double defaultValue, double minimum, double maximum)
    {
        var effect = new LowPolyAbstractionEffect();

        var animation = (Animation)Property(name).GetValue(effect)!;

        Assert.Equal(defaultValue, animation.DefaultValue);
        Assert.Equal(minimum, animation.MinValue);
        Assert.Equal(maximum, animation.MaxValue);
        Assert.Equal(defaultValue, animation.GetValue(0, 1, EffectDescriptions.Fps));
    }

    [Fact]
    public void QualityAndSeedStartFromTheirDefaults()
    {
        var effect = new LowPolyAbstractionEffect();

        Assert.Equal(LowPolyAbstractionQuality.High, effect.Quality);
        Assert.Equal(0, effect.Seed);
    }

    [Theory]
    [InlineData(int.MinValue, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1234, 1234)]
    [InlineData(10000, 10000)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void SeedNeverDropsBelowZero(int value, int expected)
    {
        var effect = new LowPolyAbstractionEffect { Seed = 5 };

        effect.Seed = value;

        Assert.Equal(expected, effect.Seed);
        Assert.False(effect.HasErrors);
    }

    [Fact]
    public void ChangingQualityOrSeedNotifiesTheEditor()
    {
        var effect = new LowPolyAbstractionEffect();
        var changed = new List<string?>();
        effect.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        effect.Quality = LowPolyAbstractionQuality.Ultra;
        effect.Seed = 7;

        Assert.Equal([nameof(LowPolyAbstractionEffect.Quality), nameof(LowPolyAbstractionEffect.Seed)], changed);
    }

    [Fact]
    public void AssigningAnUnchangedOrClampedValueDoesNotNotify()
    {
        var effect = new LowPolyAbstractionEffect();
        var changed = new List<string?>();
        effect.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        effect.Quality = LowPolyAbstractionQuality.High;
        effect.Seed = 0;
        effect.Seed = -1;

        Assert.Empty(changed);
    }

    [Fact]
    public void TheLabelIsTheLocalizedEffectName()
    {
        var effect = new LowPolyAbstractionEffect();

        Assert.Equal(Texts.LowPolyAbstraction, effect.Label);
    }

    [Fact]
    public void TheEightNumericParametersReceiveTheAnimationParameters()
    {
        var effect = new LowPolyAbstractionEffect();

        effect.SetAnimationParameters(120, EffectDescriptions.Fps);

        Assert.All(Animations(effect), animation => Assert.Equal(120, animation.Length));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void NoExoFilterIsWrittenForAviUtl(int keyFrameIndex)
    {
        var effect = new LowPolyAbstractionEffect();

        var description = new ExoOutputDescription(new VideoInfo(), string.Empty, new AviUtlDirectories(string.Empty, string.Empty));

        Assert.Empty(effect.CreateExoVideoFilters(keyFrameIndex, description));
    }

    [Fact]
    public void TheEffectIsRegisteredForDecorationAndFilteringWithoutAviUtlSupport()
    {
        var attribute = typeof(LowPolyAbstractionEffect).GetCustomAttribute<VideoEffectAttribute>()!;

        Assert.Equal(nameof(Texts.LowPolyAbstraction), attribute.Name);
        Assert.Equal([VideoEffectCategories.Decoration, VideoEffectCategories.Filtering], attribute.Categories);
        Assert.Equal([nameof(Texts.TagLowPoly), nameof(Texts.TagTriangle), nameof(Texts.TagPolygon)], attribute.Keywords);
        Assert.False(attribute.IsAviUtlSupported);
        Assert.True(attribute.IsEffectItemSupported);
        Assert.Equal(typeof(Texts), attribute.ResourceType);
        Assert.Equal(Texts.LowPolyAbstraction, attribute.GetName());
    }

    [Theory]
    [InlineData(nameof(LowPolyAbstractionEffect.Amount), nameof(Texts.BasicGroup), nameof(Texts.Amount), nameof(Texts.AmountDescription), 0)]
    [InlineData(nameof(LowPolyAbstractionEffect.Quality), nameof(Texts.BasicGroup), nameof(Texts.Quality), nameof(Texts.QualityDescription), 1)]
    [InlineData(nameof(LowPolyAbstractionEffect.Detail), nameof(Texts.StructureGroup), nameof(Texts.Detail), nameof(Texts.DetailDescription), 10)]
    [InlineData(nameof(LowPolyAbstractionEffect.Fidelity), nameof(Texts.StructureGroup), nameof(Texts.Fidelity), nameof(Texts.FidelityDescription), 11)]
    [InlineData(nameof(LowPolyAbstractionEffect.Refine), nameof(Texts.StructureGroup), nameof(Texts.Refine), nameof(Texts.RefineDescription), 12)]
    [InlineData(nameof(LowPolyAbstractionEffect.Seed), nameof(Texts.StructureGroup), nameof(Texts.Seed), nameof(Texts.SeedDescription), 13)]
    [InlineData(nameof(LowPolyAbstractionEffect.Gradient), nameof(Texts.AppearanceGroup), nameof(Texts.Gradient), nameof(Texts.GradientDescription), 20)]
    [InlineData(nameof(LowPolyAbstractionEffect.Wireframe), nameof(Texts.AppearanceGroup), nameof(Texts.Wireframe), nameof(Texts.WireframeDescription), 21)]
    [InlineData(nameof(LowPolyAbstractionEffect.Saturation), nameof(Texts.AppearanceGroup), nameof(Texts.Saturation), nameof(Texts.SaturationDescription), 22)]
    [InlineData(nameof(LowPolyAbstractionEffect.Jitter), nameof(Texts.AppearanceGroup), nameof(Texts.Jitter), nameof(Texts.JitterDescription), 23)]
    public void EveryParameterIsDisplayedInItsGroupInOrder(string property, string group, string name, string description, int order)
    {
        var display = Attribute<DisplayAttribute>(property);

        Assert.Equal(group, display.GroupName);
        Assert.Equal(name, display.Name);
        Assert.Equal(description, display.Description);
        Assert.Equal(order, display.Order);
        Assert.Equal(typeof(Texts), display.ResourceType);
    }

    [Theory]
    [InlineData(nameof(LowPolyAbstractionEffect.Amount))]
    [InlineData(nameof(LowPolyAbstractionEffect.Detail))]
    [InlineData(nameof(LowPolyAbstractionEffect.Fidelity))]
    [InlineData(nameof(LowPolyAbstractionEffect.Refine))]
    [InlineData(nameof(LowPolyAbstractionEffect.Gradient))]
    [InlineData(nameof(LowPolyAbstractionEffect.Wireframe))]
    [InlineData(nameof(LowPolyAbstractionEffect.Saturation))]
    [InlineData(nameof(LowPolyAbstractionEffect.Jitter))]
    public void AnimatedParametersAreEditedAsPercentagesWithAnimationSliders(string property)
    {
        var slider = Attribute<AnimationSliderAttribute>(property);

        Assert.Equal("F1", slider.StringFormat);
        Assert.Equal("%", slider.UnitText);
        Assert.Equal(0d, slider.DefaultMin);
        Assert.Equal(100d, slider.DefaultMax);
    }

    [Fact]
    public void TheQualityIsChosenFromACombo()
    {
        Assert.NotNull(Attribute<EnumComboBoxAttribute>(nameof(LowPolyAbstractionEffect.Quality)));
        Assert.Equal([LowPolyAbstractionQuality.Balanced, LowPolyAbstractionQuality.High, LowPolyAbstractionQuality.Ultra], Enum.GetValues<LowPolyAbstractionQuality>());
    }

    [Theory]
    [InlineData(LowPolyAbstractionQuality.Balanced, nameof(Texts.QualityBalanced), nameof(Texts.QualityBalancedDescription))]
    [InlineData(LowPolyAbstractionQuality.High, nameof(Texts.QualityHigh), nameof(Texts.QualityHighDescription))]
    [InlineData(LowPolyAbstractionQuality.Ultra, nameof(Texts.QualityUltra), nameof(Texts.QualityUltraDescription))]
    public void EveryQualityIsDisplayedWithItsLocalizedName(LowPolyAbstractionQuality quality, string name, string description)
    {
        var display = typeof(LowPolyAbstractionQuality).GetField(quality.ToString())!.GetCustomAttribute<DisplayAttribute>()!;

        Assert.Equal(name, display.Name);
        Assert.Equal(description, display.Description);
        Assert.Equal(typeof(Texts), display.ResourceType);
    }

    [Fact]
    public void TheSeedIsEditedWithoutAUnitFromZero()
    {
        var slider = Attribute<TextBoxSliderAttribute>(nameof(LowPolyAbstractionEffect.Seed));
        var range = Attribute<RangeAttribute>(nameof(LowPolyAbstractionEffect.Seed));

        Assert.Equal("F0", slider.StringFormat);
        Assert.Equal(string.Empty, slider.UnitText);
        Assert.Equal(0d, slider.DefaultMin);
        Assert.Equal(10000d, slider.DefaultMax);
        Assert.Equal(0, range.Minimum);
        Assert.Equal(int.MaxValue, range.Maximum);
        Assert.Equal(0, Attribute<DefaultValueAttribute>(nameof(LowPolyAbstractionEffect.Seed)).Value);
    }

    [Fact]
    public void EverySettingSurvivesAProjectRoundTrip()
    {
        var effect = new LowPolyAbstractionEffect { Quality = LowPolyAbstractionQuality.Ultra, Seed = 42 };
        var values = new[] { 55d, 45d, 40d, 20d, 65d, 15d, 85d, 35d };
        foreach (var (animation, value) in Animations(effect).Zip(values))
            animation.Values[0].Value = value;

        var clone = Json.GetClone(effect)!;

        Assert.NotSame(effect, clone);
        Assert.Equal(LowPolyAbstractionQuality.Ultra, clone.Quality);
        Assert.Equal(42, clone.Seed);
        Assert.Equal(values, Animations(clone).Select(animation => animation.GetValue(0, 1, EffectDescriptions.Fps)));
    }
}
