using ComputeWeave;
using ComputeWeave.Interop;

namespace LowPolyAbstraction.Tests;

[Collection("Direct3D12")]
public sealed class LowPolyAbstractionPipelineTests
{
    const int Red = unchecked((int)0xFFC04040);
    const int Blue = unchecked((int)0xFF4040C0);

    static LowPolyAbstractionPipeline CreatePipeline()
    {
        var pipeline = LowPolyAbstractionPipeline.TryCreate();
        if (pipeline is null)
            Assert.Skip("Direct3D 12 is unavailable.");
        return pipeline;
    }

    static LowPolyAbstractionPipeline.Parameters Parameters(
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

    static int[] TwoTone(int width, int height, int left, int top, int squareWidth, int squareHeight)
    {
        var pixels = new int[width * height];
        for (var y = Math.Max(top, 0); y < Math.Min(top + squareHeight, height); y++)
        {
            for (var x = Math.Max(left, 0); x < Math.Min(left + squareWidth, width); x++)
                pixels[y * width + x] = x - left < squareWidth / 2 ? Red : Blue;
        }

        return pixels;
    }

    static int[] Noise(int width, int height, int left, int top, int squareWidth, int squareHeight)
    {
        var pixels = new int[width * height];
        for (var y = top; y < top + squareHeight; y++)
        {
            for (var x = left; x < left + squareWidth; x++)
            {
                var value = (uint)(x * 73856093 ^ y * 19349663);
                value ^= value >> 13;
                value *= 0x85EBCA6Bu;
                var gray = (int)(value >> 24);
                pixels[y * width + x] = unchecked((int)0xFF000000) | gray << 16 | gray << 8 | gray;
            }
        }

        return pixels;
    }

    static int Alpha(int pixel) => (pixel >> 24) & 255;

    static int LitPixels(int[] pixels) => pixels.Count(pixel => Alpha(pixel) > 8);

    static void Upload(ReadWriteTexture2D<Bgra32, Float4> texture, int[] pixels)
        => texture.CopyFrom(pixels.Select(pixel => new Bgra32 { PackedValue = unchecked((uint)pixel) }).ToArray());

    static int[] Render(LowPolyAbstractionPipeline pipeline, int[] source, int width, int height, LowPolyAbstractionPipeline.Parameters parameters)
    {
        var destination = new int[source.Length];
        pipeline.Process(source, destination, width, height, in parameters);
        return destination;
    }

    [Fact]
    public void ATransparentSourceProducesNoTriangles()
    {
        using var pipeline = CreatePipeline();
        var destination = Enumerable.Repeat(-1, 64 * 64).ToArray();
        var parameters = Parameters();

        pipeline.Process(new int[64 * 64], destination, 64, 64, in parameters);

        Assert.All(destination, pixel => Assert.Equal(0, pixel));
    }

    [Fact]
    public void AnOpaqueSourceProducesTriangles()
    {
        using var pipeline = CreatePipeline();

        var rendering = Render(pipeline, TwoTone(128, 128, 24, 24, 80, 80), 128, 128, Parameters());

        Assert.True(LitPixels(rendering) > 0);
    }

    [Fact]
    public void TheSameSettingsAlwaysProduceTheSameTriangles()
    {
        using var pipeline = CreatePipeline();
        var source = TwoTone(128, 128, 24, 24, 80, 80);

        var first = Render(pipeline, source, 128, 128, Parameters(jitter: 0.5f, seed: 42));
        var second = Render(pipeline, source, 128, 128, Parameters(jitter: 0.5f, seed: 42));

        Assert.Equal(first, second);
    }

    [Fact]
    public void ADifferentSeedPlacesTheTrianglesDifferently()
    {
        using var pipeline = CreatePipeline();
        var source = TwoTone(128, 128, 24, 24, 80, 80);

        var first = Render(pipeline, source, 128, 128, Parameters(seed: 1));
        var second = Render(pipeline, source, 128, 128, Parameters(seed: 2));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void TheTrianglesStayPremultiplied()
    {
        using var pipeline = CreatePipeline();

        var rendering = Render(pipeline, TwoTone(128, 128, 24, 24, 80, 80), 128, 128, Parameters(gradient: 1f, wireframe: 1f, saturation: 1f, jitter: 1f));

        Assert.All(rendering, pixel =>
        {
            Assert.InRange((pixel >> 16) & 255, 0, Alpha(pixel));
            Assert.InRange((pixel >> 8) & 255, 0, Alpha(pixel));
            Assert.InRange(pixel & 255, 0, Alpha(pixel));
        });
    }

    [Fact]
    public void MoreDetailChangesTheTriangulation()
    {
        using var pipeline = CreatePipeline();
        var source = TwoTone(128, 128, 24, 24, 80, 80);

        var coarse = Render(pipeline, source, 128, 128, Parameters(detail: 0.1f));
        var fine = Render(pipeline, source, 128, 128, Parameters(detail: 1f));

        Assert.True(LitPixels(coarse) > 0);
        Assert.True(LitPixels(fine) > 0);
        Assert.NotEqual(coarse, fine);
    }

    [Fact]
    public void RefinementChangesTheTrianglesOfANoisySource()
    {
        using var pipeline = CreatePipeline();
        var source = Noise(128, 128, 16, 16, 96, 96);

        var without = Render(pipeline, source, 128, 128, Parameters(refine: 0f));
        var with = Render(pipeline, source, 128, 128, Parameters(refine: 1f));

        Assert.True(LitPixels(without) > 0);
        Assert.NotEqual(without, with);
    }

    [Fact]
    public void TheTrianglesStayWithinTheSilhouetteAndItsPadding()
    {
        using var pipeline = CreatePipeline();
        var padding = LowPolyAbstractionSettings.MarginPadding;

        var rendering = Render(pipeline, TwoTone(192, 192, 64, 64, 48, 48), 192, 192, Parameters());

        for (var y = 0; y < 192; y++)
        {
            for (var x = 0; x < 192; x++)
            {
                if (Alpha(rendering[y * 192 + x]) == 0)
                    continue;
                Assert.InRange(x, 64 - padding, 64 + 48 + padding);
                Assert.InRange(y, 64 - padding, 64 + 48 + padding);
            }
        }
    }

    [Fact]
    public void AWarmPipelineAllocatesNoManagedMemory()
    {
        using var pipeline = CreatePipeline();
        var source = TwoTone(64, 64, 16, 16, 32, 32);
        var destination = new int[source.Length];
        var parameters = Parameters();
        for (var iteration = 0; iteration < 4; iteration++)
            pipeline.Process(source, destination, 64, 64, in parameters);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var minimum = long.MaxValue;
        for (var iteration = 0; iteration < 16; iteration++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            pipeline.Process(source, destination, 64, 64, in parameters);
            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0, minimum);
    }

    [Fact]
    public void SharedTexturesProduceTheSameTrianglesAsPackedBuffers()
    {
        using var pipeline = CreatePipeline();
        var source = TwoTone(96, 96, 24, 24, 48, 48);
        var parameters = Parameters(seed: 11);
        var expected = Render(pipeline, source, 96, 96, parameters);
        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, 96, 96);
        using var outputTexture = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, 96, 96);
        Upload(sourceTexture, source);

        pipeline.ProcessSharedAndWait(sourceTexture, outputTexture, 96, 96, in parameters);
        var result = new Bgra32[source.Length];
        outputTexture.CopyTo(result);

        Assert.Equal(expected.Select(pixel => unchecked((uint)pixel)), result.Select(pixel => pixel.PackedValue));
    }

    [Fact]
    public void RepeatedSharedTextureSubmissionsAllocateNoManagedMemory()
    {
        using var pipeline = CreatePipeline();
        var device = GraphicsDevice.GetDefault();
        using var source = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, 64, 64);
        using var destination = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, 64, 64);
        var parameters = Parameters();
        for (var iteration = 0; iteration < 4; iteration++)
            pipeline.Process(source, destination, 64, 64, in parameters);
        pipeline.WaitForCompletion();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var minimum = long.MaxValue;
        for (var iteration = 0; iteration < 16; iteration++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            pipeline.Process(source, destination, 64, 64, in parameters);
            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        pipeline.WaitForCompletion();

        Assert.Equal(0, minimum);
    }

    [Fact]
    public void TheVisibleBoundsHoldEveryLitPixelAndRenderTheSameTriangles()
    {
        using var pipeline = CreatePipeline();
        var source = TwoTone(192, 192, 64, 64, 48, 40);
        var parameters = Parameters(seed: 5);
        var full = Render(pipeline, source, 192, 192, parameters);
        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(192, 192);
        Upload(sourceTexture, source);

        pipeline.Simulate(sourceTexture, 192, 192, 0, 0, 192, 192, in parameters);
        Assert.True(pipeline.TryGetVisibleBounds(192, 192, in parameters, out var rect));
        using var outputTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(rect.Width, rect.Height);
        pipeline.RenderVisible(outputTexture, 192, 192, rect, in parameters);
        var visible = new Bgra32[rect.Width * rect.Height];
        outputTexture.CopyTo(visible);

        Assert.True(rect is { Width: > 0, Height: > 0, X: >= 0, Y: >= 0 });
        Assert.True(rect.X + rect.Width <= 192 && rect.Y + rect.Height <= 192);
        for (var y = 0; y < 192; y++)
        {
            for (var x = 0; x < 192; x++)
            {
                var inside = x >= rect.X && x < rect.X + rect.Width && y >= rect.Y && y < rect.Y + rect.Height;
                if (!inside)
                    Assert.Equal(0, full[y * 192 + x]);
                else
                    Assert.Equal(unchecked((uint)full[y * 192 + x]), visible[(y - rect.Y) * rect.Width + x - rect.X].PackedValue);
            }
        }
    }

    static LowPolyAbstractionPipeline.Parameters Changed(LowPolyAbstractionPipeline.Parameters parameters, string setting)
        => setting switch
        {
            nameof(LowPolyAbstractionPipeline.Parameters.Quality) => parameters with { Quality = LowPolyAbstractionQuality.High },
            nameof(LowPolyAbstractionPipeline.Parameters.Detail) => parameters with { Detail = 0.9f },
            nameof(LowPolyAbstractionPipeline.Parameters.Fidelity) => parameters with { Fidelity = 0.2f },
            nameof(LowPolyAbstractionPipeline.Parameters.Refine) => parameters with { Refine = 0.9f },
            nameof(LowPolyAbstractionPipeline.Parameters.Seed) => parameters with { Seed = 4 },
            nameof(LowPolyAbstractionPipeline.Parameters.Gradient) => parameters with { Gradient = 1f },
            nameof(LowPolyAbstractionPipeline.Parameters.Wireframe) => parameters with { Wireframe = 1f },
            nameof(LowPolyAbstractionPipeline.Parameters.Saturation) => parameters with { Saturation = 1f },
            nameof(LowPolyAbstractionPipeline.Parameters.Jitter) => parameters with { Jitter = 1f },
            _ => throw new ArgumentOutOfRangeException(nameof(setting), setting, null),
        };

    static ReadWriteTexture2D<Bgra32, Float4> UploadedSquare()
    {
        var texture = GraphicsDevice.GetDefault().AllocateReadWriteTexture2D<Bgra32, Float4>(128, 128);
        Upload(texture, TwoTone(128, 128, 24, 24, 80, 80));
        return texture;
    }

    [Theory]
    [InlineData(nameof(LowPolyAbstractionPipeline.Parameters.Gradient))]
    [InlineData(nameof(LowPolyAbstractionPipeline.Parameters.Wireframe))]
    [InlineData(nameof(LowPolyAbstractionPipeline.Parameters.Saturation))]
    [InlineData(nameof(LowPolyAbstractionPipeline.Parameters.Jitter))]
    public void ChangingOnlyTheDrawingKeepsTheMesh(string setting)
    {
        using var pipeline = CreatePipeline();
        using var source = UploadedSquare();
        var parameters = Parameters(seed: 3);
        Assert.True(pipeline.Simulate(source, 128, 128, 0, 0, 128, 128, in parameters));

        var changed = Changed(parameters, setting);

        Assert.False(pipeline.Simulate(source, 128, 128, 0, 0, 128, 128, in changed), setting);
    }

    [Theory]
    [InlineData(nameof(LowPolyAbstractionPipeline.Parameters.Quality))]
    [InlineData(nameof(LowPolyAbstractionPipeline.Parameters.Detail))]
    [InlineData(nameof(LowPolyAbstractionPipeline.Parameters.Fidelity))]
    [InlineData(nameof(LowPolyAbstractionPipeline.Parameters.Refine))]
    [InlineData(nameof(LowPolyAbstractionPipeline.Parameters.Seed))]
    public void ChangingTheMeshSettingsComputesTheMeshAgain(string setting)
    {
        using var pipeline = CreatePipeline();
        using var source = UploadedSquare();
        var parameters = Parameters(seed: 3);
        Assert.True(pipeline.Simulate(source, 128, 128, 0, 0, 128, 128, in parameters));
        Assert.False(pipeline.Simulate(source, 128, 128, 0, 0, 128, 128, in parameters));

        var changed = Changed(parameters, setting);

        Assert.True(pipeline.Simulate(source, 128, 128, 0, 0, 128, 128, in changed), setting);
    }

    [Fact]
    public void MovingTheShapeComputesTheMeshAgain()
    {
        using var pipeline = CreatePipeline();
        using var source = UploadedSquare();
        var parameters = Parameters(seed: 3);
        Assert.True(pipeline.Simulate(source, 128, 128, 0, 0, 128, 128, in parameters));
        Assert.False(pipeline.Simulate(source, 128, 128, 0, 0, 128, 128, in parameters));

        Upload(source, TwoTone(128, 128, 8, 8, 80, 80));

        Assert.True(pipeline.Simulate(source, 128, 128, 0, 0, 128, 128, in parameters));
    }
}
