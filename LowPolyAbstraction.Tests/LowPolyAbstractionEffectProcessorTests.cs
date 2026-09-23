using System.Globalization;
using ComputeWeave;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Json;
using YukkuriMovieMaker.Player.Video;

namespace LowPolyAbstraction.Tests;

[Collection("Direct2D")]
public sealed class LowPolyAbstractionEffectProcessorTests
{
    const int Size = 64;
    const int Length = 30;
    const int Start = 16;
    const int End = 48;

    static Bgra Pattern(int x, int y)
    {
        var noise = (int)(((uint)(x * 73856093 ^ y * 19349663) * 0x85EBCA6Bu) >> 26);
        return Bgra.Opaque((byte)(64 + x * 2 + noise), (byte)(96 + noise), (byte)(224 - x * 2 - noise));
    }

    static Bgra CenteredSquare(int x, int y) => x is >= Start and < End && y is >= Start and < End ? Pattern(x, y) : Bgra.Transparent;

    static void RequireInterop(IGraphicsDevicesAndContext devices)
    {
        using var scheduler = ComputeExternalQueueScheduler.Create();
        using var provider = LowPolyAbstractionInteropProvider.TryCreate(devices, scheduler, out var device);
        if (provider is null || device is null)
            Assert.Skip("Direct3D 11 and Direct3D 12 sharing is unavailable.");
    }

    static Animation Linear(double from, double to)
        => Json.LoadFromText<Animation>(string.Create(CultureInfo.InvariantCulture, $$"""{"AnimationType":"直線移動","Values":[{"Value":{{from}}},{"Value":{{to}}}]}"""))!;

    static Rendering RenderFrame(IGraphicsDevicesAndContext devices, IVideoEffectProcessor processor, int frame)
    {
        processor.Update(EffectDescriptions.At(frame, Length));
        return Rendering.Capture(devices, processor.Output);
    }

    static void AssertSameAsSource(Rendering rendering, SourceImage source)
    {
        Assert.Equal((0, 0, source.Width, source.Height), (rendering.Left, rendering.Top, rendering.Width, rendering.Height));
        Assert.All(rendering.Coordinates(), point => Assert.Equal(source[point.X, point.Y], rendering[point.X, point.Y]));
    }

    static bool IsRedrawn(Rendering rendering, SourceImage source)
        => rendering.Coordinates().Any(point => source.Contains(point.X, point.Y) && source[point.X, point.Y].Alpha > 0 && rendering[point.X, point.Y] != source[point.X, point.Y]);

    [Fact]
    public void TheProcessorHandsTheDrawDescriptionBackUnchanged()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        using var processor = new LowPolyAbstractionEffect().CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);
        var description = EffectDescriptions.At(0, Length);

        var draw = processor.Update(description);

        Assert.Same(description.DrawDescription, draw);
    }

    [Fact]
    public void TheSquareIsRedrawnAsTrianglesWithinItsPadding()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        using var processor = new LowPolyAbstractionEffect().CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);
        var padding = LowPolyAbstractionSettings.MarginPadding;

        var rendering = RenderFrame(context, processor, 0);

        Assert.True(IsRedrawn(rendering, source));
        Assert.All(rendering.Coordinates(), point =>
        {
            var pixel = rendering[point.X, point.Y];
            Assert.InRange(pixel.Blue, 0, pixel.Alpha);
            Assert.InRange(pixel.Green, 0, pixel.Alpha);
            Assert.InRange(pixel.Red, 0, pixel.Alpha);
            if (point.X < Start - padding || point.X > End + padding || point.Y < Start - padding || point.Y > End + padding)
                Assert.Equal(Bgra.Transparent, pixel);
        });
    }

    [Fact]
    public void AZeroAmountPassesTheImageThrough()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        var effect = new LowPolyAbstractionEffect();
        effect.Amount.Values[0].Value = 0d;
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var rendering = RenderFrame(context, processor, 0);

        AssertSameAsSource(rendering, source);
    }

    [Fact]
    public void ASourceBelowTheAlphaThresholdPassesThrough()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, (x, y) => CenteredSquare(x, y) with { Alpha = (byte)(CenteredSquare(x, y).Alpha / 32) });
        using var processor = new LowPolyAbstractionEffect().CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var rendering = RenderFrame(context, processor, 0);

        AssertSameAsSource(rendering, source);
    }

    [Fact]
    public void ReturningToAFrameReproducesItExactly()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        using var processor = new LowPolyAbstractionEffect().CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var first = RenderFrame(context, processor, 4);
        RenderFrame(context, processor, 9);
        var again = RenderFrame(context, processor, 4);

        Assert.True(first.SamePixelsAs(again));
    }

    [Fact]
    public void ANewProcessorDrawsTheSameTriangles()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        var effect = new LowPolyAbstractionEffect { Seed = 9 };
        using var firstProcessor = effect.CreateVideoEffect(context);
        firstProcessor.SetInput(source.Bitmap);
        var first = RenderFrame(context, firstProcessor, 0);
        using var secondProcessor = effect.CreateVideoEffect(context);
        secondProcessor.SetInput(source.Bitmap);

        var second = RenderFrame(context, secondProcessor, 0);

        Assert.True(first.SamePixelsAs(second));
    }

    [Fact]
    public void AnimatedAmountIsReadAtEachFrame()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        var effect = new LowPolyAbstractionEffect();
        effect.Amount.CopyFrom(Linear(0d, 100d));
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var start = RenderFrame(context, processor, 0);
        var end = RenderFrame(context, processor, Length - 1);

        AssertSameAsSource(start, source);
        Assert.True(IsRedrawn(end, source));
    }

    public static readonly TheoryData<string, Action<LowPolyAbstractionEffect>> LaterChanges = new()
    {
        { nameof(LowPolyAbstractionEffect.Amount), effect => effect.Amount.Values[0].Value = 50d },
        { nameof(LowPolyAbstractionEffect.Quality), effect => effect.Quality = LowPolyAbstractionQuality.Balanced },
        { nameof(LowPolyAbstractionEffect.Detail), effect => effect.Detail.Values[0].Value = 0d },
        { nameof(LowPolyAbstractionEffect.Fidelity), effect => effect.Fidelity.Values[0].Value = 0d },
        { nameof(LowPolyAbstractionEffect.Refine), effect => effect.Refine.Values[0].Value = 100d },
        { nameof(LowPolyAbstractionEffect.Seed), effect => effect.Seed = 1 },
        { nameof(LowPolyAbstractionEffect.Gradient), effect => effect.Gradient.Values[0].Value = 100d },
        { nameof(LowPolyAbstractionEffect.Wireframe), effect => effect.Wireframe.Values[0].Value = 100d },
        { nameof(LowPolyAbstractionEffect.Saturation), effect => effect.Saturation.Values[0].Value = 100d },
        { nameof(LowPolyAbstractionEffect.Jitter), effect => effect.Jitter.Values[0].Value = 100d },
    };

    [Theory]
    [MemberData(nameof(LaterChanges))]
    public void EverySettingChangedAfterTheFirstFrameReachesTheEffect(string setting, Action<LowPolyAbstractionEffect> change)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var source = new SourceImage(context, Size, Size, CenteredSquare);
        var effect = new LowPolyAbstractionEffect();
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var before = RenderFrame(context, processor, 0);
        change(effect);
        var after = RenderFrame(context, processor, 0);

        Assert.False(before.SamePixelsAs(after), setting);
    }

    [Theory]
    [InlineData(32, 128)]
    [InlineData(128, 32)]
    public void AResizedSourceKeepsProducingTriangles(int firstSize, int secondSize)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        RequireInterop(context);
        using var first = new SourceImage(context, firstSize, firstSize, (x, y) => Pattern(x * Size / firstSize, y * Size / firstSize));
        using var second = new SourceImage(context, secondSize, secondSize, (x, y) => Pattern(x * Size / secondSize, y * Size / secondSize));
        using var processor = new LowPolyAbstractionEffect().CreateVideoEffect(context);
        processor.SetInput(first.Bitmap);
        var before = RenderFrame(context, processor, 0);

        processor.SetInput(second.Bitmap);
        var after = RenderFrame(context, processor, 0);

        Assert.True(IsRedrawn(before, first));
        Assert.True(IsRedrawn(after, second));
        Assert.Equal(secondSize > firstSize, after.Width > before.Width);
    }

    [Fact]
    public void AFailureWhileUpdatingIsNotSwallowed()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var processor = new LowPolyAbstractionEffect().CreateVideoEffect(context);

        Assert.ThrowsAny<Exception>(() => processor.Update(null!));
    }
}
