using System.Numerics;
using ComputeWeave;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;

namespace LowPolyAbstraction.Tests;

[Collection("Direct2D")]
public sealed class LowPolyAbstractionInteropTests
{
    static readonly Bgra Red = Bgra.Opaque(64, 64, 192);
    static readonly Bgra Blue = Bgra.Opaque(192, 64, 64);

    static LowPolyAbstractionPipeline.Parameters Parameters()
        => new(LowPolyAbstractionQuality.Balanced, 0.6f, 0.7f, 0.5f, 0.3f, 0f, 0.3f, 0.25f, 0);

    static Func<int, int, Bgra> CenteredTwoTone(int size, int square)
    {
        var start = (size - square) / 2;
        return (x, y) => x >= start && x < start + square && y >= start && y < start + square
            ? x - start < square / 2 ? Red : Blue
            : Bgra.Transparent;
    }

    sealed class Interop : IDisposable
    {
        readonly ComputeExternalQueueScheduler scheduler;
        readonly LowPolyAbstractionInteropProvider provider;
        readonly ComputeInteropDomain domain;

        public LowPolyAbstractionResourceSet Resources { get; }

        public LowPolyAbstractionPipeline Pipeline { get; }

        Interop(ComputeExternalQueueScheduler scheduler, LowPolyAbstractionInteropProvider provider, GraphicsDevice device)
        {
            this.scheduler = scheduler;
            this.provider = provider;
            domain = device.RegisterExternalDomain(provider);
            Resources = LowPolyAbstractionResourceSet.Create(device, domain);
            Pipeline = LowPolyAbstractionPipeline.TryCreate(device)!;
        }

        public static Interop Create(IGraphicsDevicesAndContext devices)
        {
            var scheduler = ComputeExternalQueueScheduler.Create();
            var provider = LowPolyAbstractionInteropProvider.TryCreate(devices, scheduler, out var device);
            if (provider is null || device is null)
            {
                scheduler.Dispose();
                Assert.Skip("Direct3D 11 and Direct3D 12 sharing is unavailable.");
            }

            return new Interop(scheduler, provider, device);
        }

        public void Draw(ID2D1Image image)
        {
            var context = provider.RenderContext;
            using var borrow = Resources.BeginSourceExternalOperation();
            var previousTarget = context.Target;
            using var target = new ID2D1Bitmap1(borrow.DangerousGetView().AddRefBitmap());
            context.Target = target;
            context.BeginDraw();
            context.Clear(null);
            context.DrawImage(image, Vector2.Zero, null, InterpolationMode.NearestNeighbor, CompositeMode.SourceCopy);
            context.EndDraw();
            context.Target = previousTarget;
        }

        public Rendering CaptureOutput(IGraphicsDevicesAndContext devices, out int width, out int height)
        {
            using var lease = Resources.AcquireOutputExternalViewLease();
            width = lease.Width;
            height = lease.Height;
            using var bitmap = new ID2D1Bitmap1(lease.DangerousGetView().AddRefBitmap());
            return Rendering.Capture(devices, bitmap);
        }

        public void Dispose()
        {
            Pipeline.Dispose();
            Resources.Dispose();
            Resources.WaitForDisposal();
            domain.Dispose();
            domain.WaitForDisposal();
            provider.Dispose();
            scheduler.Dispose();
        }
    }

    static int LitPixels(Rendering rendering, LowPolyAbstractionPipeline.PixelRect visible)
    {
        var lit = 0;
        for (var y = 0; y < visible.Height; y++)
        {
            for (var x = 0; x < visible.Width; x++)
            {
                var pixel = rendering[x, y];
                Assert.InRange(pixel.Blue, 0, pixel.Alpha);
                Assert.InRange(pixel.Green, 0, pixel.Alpha);
                Assert.InRange(pixel.Red, 0, pixel.Alpha);
                if (pixel.Alpha > 0)
                    lit++;
            }
        }

        return lit;
    }

    [Fact]
    public void TrianglesAreDrawnAfterTheOutputGrowsToFullHd()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var interop = Interop.Create(context);
        using var source = new SourceImage(context, 96, 96, CenteredTwoTone(96, 48));
        var parameters = Parameters();
        Assert.True(interop.Resources.TryEnsureSource(96, 96, out _));
        LowPolyAbstractionPipeline.PixelRect RenderInto(Func<LowPolyAbstractionPipeline.PixelRect, (int Width, int Height)> outputSize)
        {
            interop.Draw(source.Bitmap);
            interop.Pipeline.Simulate(interop.Resources.GetSourceComputeBinding(), 96, 96, 0, 0, 96, 96, in parameters);
            Assert.True(interop.Pipeline.TryGetVisibleBounds(96, 96, in parameters, out var visible));
            var (width, height) = outputSize(visible);
            Assert.True(interop.Resources.TryEnsureOutput(width, height, out _));
            interop.Pipeline.RenderVisible(interop.Resources.GetOutputComputeBinding(), 96, 96, visible, in parameters);
            return visible;
        }

        var first = RenderInto(visible => (visible.Width, visible.Height));
        interop.CaptureOutput(context, out var firstWidth, out var firstHeight);
        var second = RenderInto(_ => (1920, 1080));
        var output = interop.CaptureOutput(context, out var secondWidth, out var secondHeight);

        Assert.Equal((first.Width, first.Height), (firstWidth, firstHeight));
        Assert.Equal((1920, 1080), (secondWidth, secondHeight));
        Assert.True(LitPixels(output, second) > 0);
    }

    [Fact]
    public void TrianglesAreDrawnAfterTheSourceIsReplacedByALargerOne()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var interop = Interop.Create(context);
        var parameters = Parameters();
        var previous = default(LowPolyAbstractionPipeline.PixelRect);
        var previousArea = 0;

        foreach (var (size, square) in new[] { (96, 32), (128, 64) })
        {
            using var source = new SourceImage(context, size, size, CenteredTwoTone(size, square));
            Assert.True(interop.Resources.TryEnsureSource(size, size, out var sourceChanged));
            interop.Draw(source.Bitmap);
            Assert.True(interop.Pipeline.Simulate(interop.Resources.GetSourceComputeBinding(), size, size, 0, 0, size, size, in parameters));
            Assert.True(interop.Pipeline.TryGetVisibleBounds(size, size, in parameters, out var visible));
            Assert.True(interop.Resources.TryEnsureOutput(visible.Width, visible.Height, out _));
            interop.Pipeline.RenderVisible(interop.Resources.GetOutputComputeBinding(), size, size, visible, in parameters);
            var output = interop.CaptureOutput(context, out var width, out var height);

            Assert.True(sourceChanged);
            Assert.True(visible.Width > previous.Width && visible.Height > previous.Height);
            Assert.Equal((visible.Width, visible.Height), (width, height));
            Assert.True(LitPixels(output, visible) > previousArea);
            previous = visible;
            previousArea = square * square;
        }
    }
}
