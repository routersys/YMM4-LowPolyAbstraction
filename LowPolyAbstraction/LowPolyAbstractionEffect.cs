using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace LowPolyAbstraction;

[VideoEffect(nameof(Texts.LowPolyAbstraction), [VideoEffectCategories.Decoration, VideoEffectCategories.Filtering], [nameof(Texts.TagLowPoly), nameof(Texts.TagTriangle), nameof(Texts.TagPolygon)], IsAviUtlSupported = false, ResourceType = typeof(Texts))]
public sealed class LowPolyAbstractionEffect : VideoEffectBase
{
    public override string Label => Texts.LowPolyAbstraction;

    public LowPolyAbstractionEffect()
    {
        LowPolyAbstractionTelemetry.EnsureStartedOnce();
        LowPolyAbstractionUpdateNotifier.EnsureCheckedOnce();
    }

    [Display(GroupName = nameof(Texts.BasicGroup), Name = nameof(Texts.Amount), Description = nameof(Texts.AmountDescription), Order = 0, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Amount { get; } = new Animation(100, 0, 100);

    [Display(GroupName = nameof(Texts.BasicGroup), Name = nameof(Texts.Quality), Description = nameof(Texts.QualityDescription), Order = 1, ResourceType = typeof(Texts))]
    [EnumComboBox]
    public LowPolyAbstractionQuality Quality { get => _quality; set => Set(ref _quality, value); }
    private LowPolyAbstractionQuality _quality = LowPolyAbstractionQuality.High;

    [Display(GroupName = nameof(Texts.StructureGroup), Name = nameof(Texts.Detail), Description = nameof(Texts.DetailDescription), Order = 10, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Detail { get; } = new Animation(60, 0, 100);

    [Display(GroupName = nameof(Texts.StructureGroup), Name = nameof(Texts.Fidelity), Description = nameof(Texts.FidelityDescription), Order = 11, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Fidelity { get; } = new Animation(70, 0, 100);

    [Display(GroupName = nameof(Texts.StructureGroup), Name = nameof(Texts.Refine), Description = nameof(Texts.RefineDescription), Order = 12, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Refine { get; } = new Animation(50, 0, 100);

    [Display(GroupName = nameof(Texts.StructureGroup), Name = nameof(Texts.Seed), Description = nameof(Texts.SeedDescription), Order = 13, ResourceType = typeof(Texts))]
    [Range(0, int.MaxValue)]
    [DefaultValue(0)]
    [TextBoxSlider("F0", "", 0, 10000)]
    public int Seed
    {
        get => _seed;
        set => Set(ref _seed, Math.Max(value, 0));
    }
    private int _seed;

    [Display(GroupName = nameof(Texts.AppearanceGroup), Name = nameof(Texts.Gradient), Description = nameof(Texts.GradientDescription), Order = 20, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Gradient { get; } = new Animation(30, 0, 100);

    [Display(GroupName = nameof(Texts.AppearanceGroup), Name = nameof(Texts.Wireframe), Description = nameof(Texts.WireframeDescription), Order = 21, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Wireframe { get; } = new Animation(0, 0, 100);

    [Display(GroupName = nameof(Texts.AppearanceGroup), Name = nameof(Texts.Saturation), Description = nameof(Texts.SaturationDescription), Order = 22, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Saturation { get; } = new Animation(30, 0, 100);

    [Display(GroupName = nameof(Texts.AppearanceGroup), Name = nameof(Texts.Jitter), Description = nameof(Texts.JitterDescription), Order = 23, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Jitter { get; } = new Animation(25, 0, 100);

    private IAnimatable[]? _animatables;

    public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];

    public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
    {
        try
        {
            return new LowPolyAbstractionEffectProcessor(devices, this);
        }
        catch (Exception exception)
        {
            LowPolyAbstractionTelemetry.Report(exception);
            throw;
        }
    }

    protected override IEnumerable<IAnimatable> GetAnimatables()
        => _animatables ??= [Amount, Detail, Fidelity, Refine, Gradient, Wireframe, Saturation, Jitter];
}
