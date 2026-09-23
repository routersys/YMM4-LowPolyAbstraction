using System.Runtime.InteropServices;
using ComputeWeave;
using ComputeWeave.Interop;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;
using PixelFormat = Vortice.DCommon.PixelFormat;

namespace LowPolyAbstraction.Tests;

public sealed class LowPolyAbstractionEffectTests
{
    private static double ValueAt(YukkuriMovieMaker.Commons.Animation animation) => animation.GetValue(0, 1, 30);

    private static LowPolyAbstractionPipeline.Parameters CreateParameters(
        LowPolyAbstractionQuality quality = LowPolyAbstractionQuality.Balanced,
        float detail = 0.6f,
        float fidelity = 0.7f,
        float refine = 0.5f,
        float gradient = 0.3f,
        float wireframe = 0f,
        float saturation = 0.3f,
        float jitter = 0.25f,
        int seed = 0)
        => new(quality, detail, fidelity, refine, gradient, wireframe, saturation, jitter, seed);

    [Fact]
    public void DefaultParameterValuesMatchSpecification()
    {
        var effect = new LowPolyAbstractionEffect();

        Assert.Equal(100d, ValueAt(effect.Amount), 6);
        Assert.Equal(60d, ValueAt(effect.Detail), 6);
        Assert.Equal(70d, ValueAt(effect.Fidelity), 6);
        Assert.Equal(50d, ValueAt(effect.Refine), 6);
        Assert.Equal(30d, ValueAt(effect.Gradient), 6);
        Assert.Equal(0d, ValueAt(effect.Wireframe), 6);
        Assert.Equal(30d, ValueAt(effect.Saturation), 6);
        Assert.Equal(25d, ValueAt(effect.Jitter), 6);
        Assert.Equal(LowPolyAbstractionQuality.High, effect.Quality);
        Assert.Equal(0, effect.Seed);
    }

    [Theory]
    [InlineData(int.MinValue, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1234, 1234)]
    public void SeedClampsNegativeInputToZero(int input, int expected)
    {
        var effect = new LowPolyAbstractionEffect { Seed = input };

        Assert.Equal(expected, effect.Seed);
    }

    [Fact]
    public void CreateExoVideoFiltersReturnsEmpty()
    {
        var effect = new LowPolyAbstractionEffect();

        Assert.Empty(effect.CreateExoVideoFilters(0, null!));
    }

    [Theory]
    [InlineData(LowPolyAbstractionQuality.Balanced, 1024, 4096, 6, 1)]
    [InlineData(LowPolyAbstractionQuality.High, 1536, 8192, 8, 2)]
    [InlineData(LowPolyAbstractionQuality.Ultra, 2048, 16384, 10, 3)]
    public void QualitySettingsMatchSpecification(LowPolyAbstractionQuality quality, int resolution, int sites, int iterations, int refinePasses)
    {
        var settings = LowPolyAbstractionSettings.GetQuality(quality);

        Assert.Equal(resolution, settings.WorkingResolution);
        Assert.Equal(sites, settings.BaseSites);
        Assert.Equal(iterations, settings.CvtIterations);
        Assert.Equal(refinePasses, settings.RefinePasses);
    }

    [Theory]
    [InlineData(1920, 1080, 1536)]
    [InlineData(1080, 1920, 1536)]
    [InlineData(8, 8, 1536)]
    [InlineData(4096, 16, 1024)]
    [InlineData(100, 100, 2048)]
    public void WorkingSizeCoversCanvas(int width, int height, int resolution)
    {
        var (workingWidth, workingHeight, scale) = LowPolyAbstractionSettings.GetWorkingSize(width, height, resolution);

        Assert.True(workingWidth >= LowPolyAbstractionSettings.MinimumWorkingSize);
        Assert.True(workingHeight >= LowPolyAbstractionSettings.MinimumWorkingSize);
        Assert.True(scale > 0f);
        Assert.True(workingWidth * scale >= width);
        Assert.True(workingHeight * scale >= height);
    }

    [Fact]
    public void ParameterMappingsAreMonotonicAndBounded()
    {
        Assert.True(LowPolyAbstractionSettings.GetSiteCount(0f, 4096) < LowPolyAbstractionSettings.GetSiteCount(1f, 4096));
        Assert.Equal(LowPolyAbstractionSettings.GetSiteCount(0f, 4096), LowPolyAbstractionSettings.GetSiteCount(-5f, 4096));
        Assert.Equal(LowPolyAbstractionSettings.GetSiteCount(1f, 4096), LowPolyAbstractionSettings.GetSiteCount(5f, 4096));
        Assert.True(LowPolyAbstractionSettings.GetSiteCount(1f, 4096) <= LowPolyAbstractionSettings.GetSiteCapacity(4096));

        Assert.True(LowPolyAbstractionSettings.GetEdgeSampleSpacing(1024, 1024, 1f) < LowPolyAbstractionSettings.GetEdgeSampleSpacing(1024, 1024, 0f));
        Assert.True(LowPolyAbstractionSettings.GetEdgeSampleSpacing(1024, 1024, 1f) >= 2f);

        Assert.True(LowPolyAbstractionSettings.GetErrorThreshold(1f) < LowPolyAbstractionSettings.GetErrorThreshold(0f));
        Assert.Equal(LowPolyAbstractionSettings.MaximumErrorThreshold, LowPolyAbstractionSettings.GetErrorThreshold(-1f), 7);
        Assert.Equal(LowPolyAbstractionSettings.MinimumErrorThreshold, LowPolyAbstractionSettings.GetErrorThreshold(2f), 7);
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 2, 1)]
    [InlineData(3, 3, 2)]
    [InlineData(256, 128, 128)]
    [InlineData(257, 16, 256)]
    public void JumpFloodInitialStepCoversLongSide(int width, int height, int expected)
    {
        Assert.Equal(expected, LowPolyAbstractionSettings.GetJumpFloodInitialStep(width, height));
    }

    [Fact]
    public void TransparentInputYieldsTransparentOutput()
    {
        using var pipeline = LowPolyAbstractionPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 64;
        const int height = 64;
        var source = new int[width * height];
        var destination = new int[source.Length];
        Array.Fill(destination, -1);
        var parameters = CreateParameters();

        pipeline.Process(source, destination, width, height, in parameters);

        Assert.All(destination, pixel => Assert.Equal(0, pixel));
    }

    [Fact]
    public void OpaqueInputProducesTriangles()
    {
        using var pipeline = LowPolyAbstractionPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateTwoToneSource(width, height, 24, 24, 80, 80);
        var destination = new int[source.Length];
        var parameters = CreateParameters();

        pipeline.Process(source, destination, width, height, in parameters);

        Assert.True(CountLitPixels(destination) > 0);
    }

    [Fact]
    public void GpuPipelineIsDeterministic()
    {
        using var pipeline = LowPolyAbstractionPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateTwoToneSource(width, height, 24, 24, 80, 80);
        var first = new int[source.Length];
        var second = new int[source.Length];
        var parameters = CreateParameters(jitter: 0.5f, seed: 42);

        pipeline.Process(source, first, width, height, in parameters);
        pipeline.Process(source, second, width, height, in parameters);

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentSeedsProduceDifferentTriangulations()
    {
        using var pipeline = LowPolyAbstractionPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateTwoToneSource(width, height, 24, 24, 80, 80);
        var first = new int[source.Length];
        var second = new int[source.Length];

        var parametersA = CreateParameters(seed: 1);
        var parametersB = CreateParameters(seed: 2);
        pipeline.Process(source, first, width, height, in parametersA);
        pipeline.Process(source, second, width, height, in parametersB);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void OutputAlphaStaysPremultipliedAndBounded()
    {
        using var pipeline = LowPolyAbstractionPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateTwoToneSource(width, height, 24, 24, 80, 80);
        var destination = new int[source.Length];
        var parameters = CreateParameters(gradient: 1f, wireframe: 1f, saturation: 1f, jitter: 1f);

        pipeline.Process(source, destination, width, height, in parameters);

        foreach (var pixel in destination)
        {
            var alpha = (pixel >> 24) & 255;
            Assert.InRange((pixel >> 16) & 255, 0, alpha);
            Assert.InRange((pixel >> 8) & 255, 0, alpha);
            Assert.InRange(pixel & 255, 0, alpha);
        }
    }

    [Fact]
    public void DetailChangesTriangulation()
    {
        using var pipeline = LowPolyAbstractionPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateTwoToneSource(width, height, 24, 24, 80, 80);
        var coarse = new int[source.Length];
        var fine = new int[source.Length];

        var coarseParameters = CreateParameters(detail: 0.1f);
        var fineParameters = CreateParameters(detail: 1f);
        pipeline.Process(source, coarse, width, height, in coarseParameters);
        pipeline.Process(source, fine, width, height, in fineParameters);

        Assert.True(CountLitPixels(coarse) > 0);
        Assert.True(CountLitPixels(fine) > 0);
        Assert.NotEqual(coarse, fine);
    }

    [Fact]
    public void RefinementChangesOutputOnHighVarianceSource()
    {
        using var pipeline = LowPolyAbstractionPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = new int[width * height];
        for (var y = 16; y < 112; y++)
        {
            for (var x = 16; x < 112; x++)
            {
                var value = (uint)(x * 73856093 ^ y * 19349663);
                value ^= value >> 13;
                value *= 0x85EBCA6Bu;
                var gray = (int)(value >> 24);
                source[y * width + x] = unchecked((int)0xFF000000) | gray << 16 | gray << 8 | gray;
            }
        }
        var without = new int[source.Length];
        var with = new int[source.Length];

        var withoutParameters = CreateParameters(refine: 0f);
        var withParameters = CreateParameters(refine: 1f);
        pipeline.Process(source, without, width, height, in withoutParameters);
        pipeline.Process(source, with, width, height, in withParameters);

        Assert.True(CountLitPixels(without) > 0);
        Assert.NotEqual(without, with);
    }

    [Fact]
    public void OutputStaysWithinSilhouetteBoundsWithPadding()
    {
        using var pipeline = LowPolyAbstractionPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 192;
        const int height = 192;
        var source = CreateTwoToneSource(width, height, 64, 64, 48, 48);
        var destination = new int[source.Length];
        var parameters = CreateParameters();

        pipeline.Process(source, destination, width, height, in parameters);

        var padding = LowPolyAbstractionSettings.MarginPadding;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (((destination[y * width + x] >> 24) & 255) == 0)
                    continue;
                Assert.InRange(x, 64 - padding, 64 + 48 + padding);
                Assert.InRange(y, 64 - padding, 64 + 48 + padding);
            }
        }
    }

    [Fact]
    public void GpuPipelineDoesNotAllocateManagedMemoryAfterWarmup()
    {
        using var pipeline = LowPolyAbstractionPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 64;
        const int height = 64;
        var source = CreateTwoToneSource(width, height, 16, 16, 32, 32);
        var destination = new int[source.Length];
        var parameters = CreateParameters();
        for (var iteration = 0; iteration < 4; iteration++)
            pipeline.Process(source, destination, width, height, in parameters);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var minimum = long.MaxValue;
        for (var iteration = 0; iteration < 16; iteration++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            pipeline.Process(source, destination, width, height, in parameters);
            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0, minimum);
    }

    [Fact]
    public void SharedTexturePipelineMatchesPackedBufferPipeline()
    {
        using var pipeline = LowPolyAbstractionPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 96;
        const int height = 96;
        var source = CreateTwoToneSource(width, height, 24, 24, 48, 48);
        var expected = new int[source.Length];
        var parameters = CreateParameters(seed: 11);
        pipeline.Process(source, expected, width, height, in parameters);

        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        using var outputTexture = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        var sourcePixels = new Bgra32[source.Length];
        for (var index = 0; index < source.Length; index++)
            sourcePixels[index].PackedValue = unchecked((uint)source[index]);
        sourceTexture.CopyFrom(sourcePixels);
        pipeline.ProcessSharedAndWait(sourceTexture, outputTexture, width, height, in parameters);
        var result = new Bgra32[source.Length];
        outputTexture.CopyTo(result);

        for (var index = 0; index < expected.Length; index++)
            Assert.Equal(unchecked((uint)expected[index]), result[index].PackedValue);
    }

    [Fact]
    public void SubmittedSharedTexturePipelineAllocationsAmortizeToZero()
    {
        using var pipeline = LowPolyAbstractionPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 64;
        const int height = 64;
        var device = GraphicsDevice.GetDefault();
        using var source = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        using var destination = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        var parameters = CreateParameters();
        for (var iteration = 0; iteration < 4; iteration++)
            pipeline.Process(source, destination, width, height, in parameters);
        pipeline.WaitForCompletion();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var minimum = long.MaxValue;
        for (var iteration = 0; iteration < 16; iteration++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            pipeline.Process(source, destination, width, height, in parameters);
            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        pipeline.WaitForCompletion();

        Assert.Equal(0, minimum);
    }

    [Fact]
    public void VisibleBoundsCoverAllLitPixelsAndMatchFullRender()
    {
        using var pipeline = LowPolyAbstractionPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 192;
        const int height = 192;
        var source = CreateTwoToneSource(width, height, 64, 64, 48, 40);
        var full = new int[source.Length];
        var parameters = CreateParameters(seed: 5);
        pipeline.Process(source, full, width, height, in parameters);

        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        var sourcePixels = new Bgra32[source.Length];
        for (var index = 0; index < source.Length; index++)
            sourcePixels[index].PackedValue = unchecked((uint)source[index]);
        sourceTexture.CopyFrom(sourcePixels);

        pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in parameters);
        Assert.True(pipeline.TryGetVisibleBounds(width, height, in parameters, out var rect));
        Assert.True(rect.Width > 0 && rect.Height > 0);
        Assert.True(rect.X >= 0 && rect.Y >= 0);
        Assert.True(rect.X + rect.Width <= width && rect.Y + rect.Height <= height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (full[y * width + x] == 0)
                    continue;
                Assert.InRange(x, rect.X, rect.X + rect.Width - 1);
                Assert.InRange(y, rect.Y, rect.Y + rect.Height - 1);
            }
        }

        using var outputTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(rect.Width, rect.Height);
        pipeline.RenderVisible(outputTexture, width, height, rect, in parameters);
        var result = new Bgra32[rect.Width * rect.Height];
        outputTexture.CopyTo(result);

        for (var y = 0; y < rect.Height; y++)
        {
            for (var x = 0; x < rect.Width; x++)
            {
                var expected = unchecked((uint)full[(rect.Y + y) * width + rect.X + x]);
                Assert.Equal(expected, result[y * rect.Width + x].PackedValue);
            }
        }
    }

    [Fact]
    public void SimulateCachesStructureUntilInputsChange()
    {
        using var pipeline = LowPolyAbstractionPipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateTwoToneSource(width, height, 24, 24, 80, 80);
        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        var sourcePixels = new Bgra32[source.Length];
        for (var index = 0; index < source.Length; index++)
            sourcePixels[index].PackedValue = unchecked((uint)source[index]);
        sourceTexture.CopyFrom(sourcePixels);

        var parameters = CreateParameters(seed: 3);
        Assert.True(pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in parameters));
        Assert.False(pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in parameters));

        var renderOnlyChanged = parameters with { Gradient = 1f, Wireframe = 1f };
        Assert.False(pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in renderOnlyChanged));

        var seedChanged = parameters with { Seed = 4 };
        Assert.True(pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in seedChanged));

        var movedSource = CreateTwoToneSource(width, height, 8, 8, 80, 80);
        for (var index = 0; index < movedSource.Length; index++)
            sourcePixels[index].PackedValue = unchecked((uint)movedSource[index]);
        sourceTexture.CopyFrom(sourcePixels);
        Assert.True(pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in seedChanged));
    }

    [Fact]
    public void Direct2DInteropProducesTrianglesAfterGrowingFullHdOutput()
    {
        using var devices = new GraphicsDevices();
        using var graphicsContext = devices.CreateContext();
        using var scheduler = ComputeExternalQueueScheduler.Create();
        using var provider = LowPolyAbstractionInteropProvider.TryCreate(graphicsContext, scheduler, out var interopDevice);
        if (provider is null || interopDevice is null)
        {
            Assert.Skip("Direct3D 11 and Direct3D 12 sharing is unavailable.");
            return;
        }

        using var domain = interopDevice.RegisterExternalDomain(provider);
        using var resourceSet = LowPolyAbstractionResourceSet.Create(interopDevice, domain);
        using var pipeline = LowPolyAbstractionPipeline.TryCreate(interopDevice);
        Assert.NotNull(pipeline);

        const int width = 96;
        const int height = 96;
        const int fullHdWidth = 1920;
        const int fullHdHeight = 1080;
        using var inputBitmap = CreateInputBitmap(graphicsContext.DeviceContext, CreateTwoToneSource(width, height, 24, 24, 48, 48), width, height);

        Assert.True(resourceSet.TryEnsureSource(width, height, out _));
        var parameters = CreateParameters();
        LowPolyAbstractionPipeline.PixelRect visible = default;
        for (var iteration = 0; iteration < 2; iteration++)
        {
            DrawSource(resourceSet, provider.RenderContext, inputBitmap);
            pipeline!.Simulate(
                resourceSet.GetSourceComputeBinding(), width, height, 0, 0, width, height, in parameters);
            Assert.True(pipeline.TryGetVisibleBounds(width, height, in parameters, out visible));
            Assert.True(resourceSet.TryEnsureOutput(
                iteration == 0 ? visible.Width : fullHdWidth,
                iteration == 0 ? visible.Height : fullHdHeight,
                out _));
            pipeline.RenderVisible(
                resourceSet.GetOutputComputeBinding(), width, height, visible, in parameters);

            if (iteration == 0)
            {
                using var retiredLease = resourceSet.AcquireOutputExternalViewLease();
                Assert.Equal(visible.Width, retiredLease.Width);
                Assert.Equal(visible.Height, retiredLease.Height);
            }
        }

        using var outputLease = resourceSet.AcquireOutputExternalViewLease();
        Assert.Equal(fullHdWidth, outputLease.Width);
        Assert.Equal(fullHdHeight, outputLease.Height);
        Assert.True(CountLitOutput(graphicsContext.DeviceContext, outputLease, visible) > 0);
    }

    [Fact]
    public void Direct2DInteropProducesTrianglesAfterReplacingSource()
    {
        using var devices = new GraphicsDevices();
        using var graphicsContext = devices.CreateContext();
        using var scheduler = ComputeExternalQueueScheduler.Create();
        using var provider = LowPolyAbstractionInteropProvider.TryCreate(graphicsContext, scheduler, out var interopDevice);
        if (provider is null || interopDevice is null)
        {
            Assert.Skip("Direct3D 11 and Direct3D 12 sharing is unavailable.");
            return;
        }

        using var domain = interopDevice.RegisterExternalDomain(provider);
        using var resourceSet = LowPolyAbstractionResourceSet.Create(interopDevice, domain);
        using var pipeline = LowPolyAbstractionPipeline.TryCreate(interopDevice);
        Assert.NotNull(pipeline);

        var parameters = CreateParameters();
        var previousVisible = default(LowPolyAbstractionPipeline.PixelRect);
        var previousArea = 0;
        foreach (var (size, square) in new[] { (96, 32), (128, 64) })
        {
            using var inputBitmap = CreateInputBitmap(graphicsContext.DeviceContext, CreateTwoToneSource(size, size, (size - square) / 2, (size - square) / 2, square, square), size, size);
            Assert.True(resourceSet.TryEnsureSource(size, size, out var sourceChanged));
            Assert.True(sourceChanged);
            DrawSource(resourceSet, provider.RenderContext, inputBitmap);

            Assert.True(pipeline!.Simulate(
                resourceSet.GetSourceComputeBinding(), size, size, 0, 0, size, size, in parameters));
            Assert.True(pipeline.TryGetVisibleBounds(size, size, in parameters, out var visible));
            Assert.True(visible.Width > previousVisible.Width && visible.Height > previousVisible.Height);
            Assert.True(resourceSet.TryEnsureOutput(visible.Width, visible.Height, out _));
            pipeline.RenderVisible(
                resourceSet.GetOutputComputeBinding(), size, size, visible, in parameters);

            using var outputLease = resourceSet.AcquireOutputExternalViewLease();
            Assert.Equal(visible.Width, outputLease.Width);
            Assert.Equal(visible.Height, outputLease.Height);
            Assert.True(CountLitOutput(graphicsContext.DeviceContext, outputLease, visible) > previousArea);
            previousVisible = visible;
            previousArea = square * square;
        }
    }

    private static ID2D1Bitmap1 CreateInputBitmap(ID2D1DeviceContext6 deviceContext, int[] pixels, int width, int height)
    {
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        var inputBitmap = deviceContext.CreateBitmap(
            new SizeI(width, height),
            new BitmapProperties1(
                new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                96f,
                96f,
                BitmapOptions.None));
        try
        {
            inputBitmap.CopyFromMemory(handle.AddrOfPinnedObject(), width * sizeof(int));
        }
        finally
        {
            handle.Free();
        }
        return inputBitmap;
    }

    private static void DrawSource(LowPolyAbstractionResourceSet resourceSet, ID2D1DeviceContext6 renderContext, ID2D1Bitmap1 inputBitmap)
    {
        using var borrow = resourceSet.BeginSourceExternalOperation();
        var previousTarget = renderContext.Target;
        using var sourceBitmap = new ID2D1Bitmap1(borrow.DangerousGetView().AddRefBitmap());
        renderContext.Target = sourceBitmap;
        renderContext.BeginDraw();
        renderContext.Clear(null);
        renderContext.DrawImage(
            inputBitmap,
            new System.Numerics.Vector2(0f, 0f),
            null,
            InterpolationMode.NearestNeighbor,
            CompositeMode.SourceCopy);
        renderContext.EndDraw();
        renderContext.Target = previousTarget;
    }

    private static int CountLitOutput(ID2D1DeviceContext6 deviceContext, ExternalTextureLease<ExternalDirect3D11TextureView> outputLease, LowPolyAbstractionPipeline.PixelRect visible)
    {
        using var staging = deviceContext.CreateBitmap(
            new SizeI(visible.Width, visible.Height),
            new BitmapProperties1(
                new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                96f,
                96f,
                BitmapOptions.CpuRead | BitmapOptions.CannotDraw));
        using var outputBitmap = new ID2D1Bitmap1(outputLease.DangerousGetView().AddRefBitmap());
        staging.CopyFromBitmap(Vortice.Mathematics.Int2.Zero, outputBitmap, new RectI(0, 0, visible.Width, visible.Height));
        var mapped = staging.Map(MapOptions.Read);
        try
        {
            var lit = 0;
            for (var y = 0; y < visible.Height; y++)
            {
                for (var x = 0; x < visible.Width; x++)
                {
                    var actual = Marshal.ReadInt32(mapped.Bits + (nint)(y * mapped.Pitch + x * sizeof(int)));
                    var alpha = (actual >> 24) & 255;
                    Assert.InRange((actual >> 16) & 255, 0, alpha);
                    Assert.InRange((actual >> 8) & 255, 0, alpha);
                    Assert.InRange(actual & 255, 0, alpha);
                    if (alpha > 0)
                        lit++;
                }
            }
            return lit;
        }
        finally
        {
            staging.Unmap();
        }
    }

    private static int[] CreateTwoToneSource(int width, int height, int left, int top, int squareWidth, int squareHeight)
    {
        var source = new int[width * height];
        for (var y = top; y < top + squareHeight; y++)
        {
            for (var x = left; x < left + squareWidth; x++)
            {
                if (x < 0 || x >= width || y < 0 || y >= height)
                    continue;
                source[y * width + x] = x - left < squareWidth / 2
                    ? unchecked((int)0xFFC04040)
                    : unchecked((int)0xFF4040C0);
            }
        }
        return source;
    }

    private static int CountLitPixels(int[] pixels)
    {
        var count = 0;
        foreach (var pixel in pixels)
        {
            if (((pixel >> 24) & 255) > 8)
                count++;
        }
        return count;
    }
}
