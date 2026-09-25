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

    static int[] Silhouette(int size, Func<int, int, bool> inside)
    {
        var pixels = new int[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var u = x * 256 / size;
                var v = y * 256 / size;
                if (inside(x, y))
                    pixels[y * size + x] = unchecked((int)0xFF000000) | (40 + u * 3 / 4) << 16 | (200 - v / 2) << 8 | (80 + (u + v) / 4);
            }
        }

        return pixels;
    }

    public enum Hollow
    {
        Notch,
        Gap,
        Hole,
    }

    static Func<int, int, bool> HollowShape(Hollow hollow)
        => hollow switch
        {
            Hollow.Notch => (x, y) => x is >= 40 and < 216 && y is >= 40 and < 216 && !(x is >= 116 and < 140 && y < 180),
            Hollow.Gap => (x, y) => y is >= 60 and < 196 && (x is >= 40 and < 110 || x is >= 146 and < 216),
            _ => (x, y) => (x - 128) * (x - 128) + (y - 128) * (y - 128) is < 100 * 100 and >= 45 * 45,
        };

    public enum FineGaps
    {
        Lattice,
        Speckles,
    }

    static Func<int, int, bool> FineGapShape(FineGaps gaps)
        => gaps switch
        {
            FineGaps.Lattice => (x, y) => x is >= 40 and < 216 && y is >= 40 and < 216 && x % 8 != 0 && y % 8 != 0,
            _ => (x, y) =>
            {
                var value = (uint)(x * 73856093 ^ y * 19349663);
                value ^= value >> 13;
                value *= 0x85EBCA6Bu;
                value ^= value >> 16;
                return x is >= 40 and < 216 && y is >= 40 and < 216 && value % 50 != 0;
            },
        };

    static bool AnyWithin(int x, int y, int reach, Func<int, int, bool> predicate)
    {
        for (var dy = -reach; dy <= reach; dy++)
        {
            for (var dx = -reach; dx <= reach; dx++)
            {
                if (predicate(x + dx, y + dy))
                    return true;
            }
        }

        return false;
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

    [Theory]
    [InlineData(LowPolyAbstractionQuality.Balanced, 0f, 128)]
    [InlineData(LowPolyAbstractionQuality.Balanced, 1f, 128)]
    [InlineData(LowPolyAbstractionQuality.High, 0f, 128)]
    [InlineData(LowPolyAbstractionQuality.High, 1f, 128)]
    [InlineData(LowPolyAbstractionQuality.High, 0f, 1600)]
    public void ASingleColorShapeIsDrawnInItsOwnColor(LowPolyAbstractionQuality quality, float gradient, int size)
    {
        using var pipeline = CreatePipeline();
        const int color = unchecked((int)0xFF3080C0);
        var start = size * 3 / 16;
        var side = size * 5 / 8;
        var source = TwoTone(size, size, start, start, side, side).Select(pixel => pixel == 0 ? 0 : color).ToArray();

        var rendering = Render(pipeline, source, size, size, Parameters(quality, gradient: gradient, saturation: 0f, jitter: 0f));

        var drawn = 0;
        for (var y = start; y < start + side; y++)
        {
            for (var x = start; x < start + side; x++)
            {
                var pixel = rendering[y * size + x];
                if (Alpha(pixel) == 0)
                    continue;
                drawn++;
                if (pixel != color)
                    Assert.Fail($"({x}, {y}) {pixel:X8}");
            }
        }
        Assert.True(drawn > side * side / 2, $"{drawn}");
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(0.2f)]
    public void TheWireframeDarkensTheSameLinesInProportionToItsValue(float wireframe)
    {
        using var pipeline = CreatePipeline();
        const int color = unchecked((int)0xFF3080C0);
        var source = TwoTone(128, 128, 24, 24, 80, 80).Select(pixel => pixel == 0 ? 0 : color).ToArray();
        int[] RenderWith(float value) => Render(pipeline, source, 128, 128, Parameters(gradient: 0f, wireframe: value, saturation: 0f, jitter: 0f, seed: 1));
        var plain = RenderWith(0f);
        var full = RenderWith(1f);

        var partial = RenderWith(wireframe);

        var lined = 0;
        for (var index = 0; index < source.Length; index++)
        {
            if (plain[index] != color)
                continue;
            if (full[index] != color)
                lined++;
            for (var shift = 0; shift < 24; shift += 8)
            {
                var channel = (color >> shift) & 255;
                var fullDarkening = channel - ((full[index] >> shift) & 255);
                var partialDarkening = channel - ((partial[index] >> shift) & 255);
                Assert.InRange(partialDarkening, fullDarkening * wireframe - 1, fullDarkening * wireframe + 1);
            }
        }
        Assert.True(lined > 128, $"{lined}");
    }

    [Theory]
    [InlineData(128, 128, LowPolyAbstractionQuality.High)]
    [InlineData(96, 96, LowPolyAbstractionQuality.High)]
    [InlineData(160, 192, LowPolyAbstractionQuality.High)]
    [InlineData(128, 96, LowPolyAbstractionQuality.High)]
    [InlineData(96, 128, LowPolyAbstractionQuality.High)]
    [InlineData(128, 128, LowPolyAbstractionQuality.Balanced)]
    public void APipelineThatDrewAnotherImageDrawsLikeAFreshOne(int previousWidth, int previousHeight, LowPolyAbstractionQuality previousQuality)
    {
        var source = TwoTone(128, 128, 24, 24, 80, 80);
        var parameters = Parameters(LowPolyAbstractionQuality.High, seed: 7);
        int[] expected;
        using (var fresh = CreatePipeline())
            expected = Render(fresh, source, 128, 128, parameters);
        using var pipeline = CreatePipeline();
        Render(pipeline, Noise(previousWidth, previousHeight, 8, 8, previousWidth - 16, previousHeight - 16), previousWidth, previousHeight, Parameters(previousQuality, seed: 3));

        var reused = Render(pipeline, source, 128, 128, parameters);

        Assert.Equal(expected, reused);
    }

    [Fact]
    public void APixelCountBeyondTheIntegerRangeIsRejected()
    {
        using var pipeline = CreatePipeline();
        var parameters = Parameters();

        Assert.Throws<OverflowException>(() => pipeline.Process([], [], 65536, 65536, in parameters));
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

    [Theory]
    [InlineData(LowPolyAbstractionQuality.Balanced)]
    [InlineData(LowPolyAbstractionQuality.High)]
    [InlineData(LowPolyAbstractionQuality.Ultra)]
    public void ADiscIsNotPaintedBeyondItsOutline(LowPolyAbstractionQuality quality)
    {
        using var pipeline = CreatePipeline();
        const int size = 256;
        static int DistanceSquared(int x, int y) => (x - 128) * (x - 128) + (y - 128) * (y - 128);
        var source = Silhouette(size, (x, y) => DistanceSquared(x, y) < 90 * 90);

        var rendering = Render(pipeline, source, size, size, Parameters(quality));

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (DistanceSquared(x, y) > 93 * 93)
                    Assert.True(Alpha(rendering[y * size + x]) == 0, $"({x}, {y})");
            }
        }
    }

    [Theory]
    [InlineData(Hollow.Notch, LowPolyAbstractionQuality.Balanced)]
    [InlineData(Hollow.Notch, LowPolyAbstractionQuality.High)]
    [InlineData(Hollow.Notch, LowPolyAbstractionQuality.Ultra)]
    [InlineData(Hollow.Gap, LowPolyAbstractionQuality.Balanced)]
    [InlineData(Hollow.Gap, LowPolyAbstractionQuality.High)]
    [InlineData(Hollow.Gap, LowPolyAbstractionQuality.Ultra)]
    [InlineData(Hollow.Hole, LowPolyAbstractionQuality.Balanced)]
    [InlineData(Hollow.Hole, LowPolyAbstractionQuality.High)]
    [InlineData(Hollow.Hole, LowPolyAbstractionQuality.Ultra)]
    public void TheHollowsOfAShapeStayTransparent(Hollow hollow, LowPolyAbstractionQuality quality)
    {
        using var pipeline = CreatePipeline();
        const int size = 256;
        var inside = HollowShape(hollow);

        var rendering = Render(pipeline, Silhouette(size, inside), size, size, Parameters(quality));

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (!AnyWithin(x, y, 3, inside))
                    Assert.True(Alpha(rendering[y * size + x]) == 0, $"({x}, {y})");
            }
        }
    }

    [Theory]
    [InlineData(Hollow.Notch, LowPolyAbstractionQuality.Balanced)]
    [InlineData(Hollow.Gap, LowPolyAbstractionQuality.High)]
    [InlineData(Hollow.Hole, LowPolyAbstractionQuality.Ultra)]
    public void TheBodyOfAHollowShapeStaysPainted(Hollow hollow, LowPolyAbstractionQuality quality)
    {
        using var pipeline = CreatePipeline();
        const int size = 256;
        var inside = HollowShape(hollow);

        var rendering = Render(pipeline, Silhouette(size, inside), size, size, Parameters(quality));

        int body = 0, painted = 0;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (AnyWithin(x, y, 3, (sx, sy) => !inside(sx, sy)))
                    continue;
                body++;
                if (Alpha(rendering[y * size + x]) > 0)
                    painted++;
            }
        }

        Assert.True(body > 10000);
        Assert.True(painted >= body * 0.99, $"{painted}/{body}");
    }

    [Theory]
    [InlineData(FineGaps.Lattice)]
    [InlineData(FineGaps.Speckles)]
    public void FineGapsStayOpenAndTheMaterialAroundThemIsPainted(FineGaps gaps)
    {
        using var pipeline = CreatePipeline();
        const int size = 256;
        var inside = FineGapShape(gaps);

        var rendering = Render(pipeline, Silhouette(size, inside), size, size, Parameters(LowPolyAbstractionQuality.High));

        int material = 0, painted = 0;
        for (var y = 44; y < 212; y++)
        {
            for (var x = 44; x < 212; x++)
            {
                var alpha = Alpha(rendering[y * size + x]);
                if (!inside(x, y))
                {
                    Assert.True(alpha == 0, $"({x}, {y})");
                    continue;
                }
                material++;
                if (alpha > 0)
                    painted++;
            }
        }

        Assert.True(painted >= material * 0.95, $"{painted}/{material}");
    }

    [Theory]
    [InlineData(LowPolyAbstractionQuality.Balanced)]
    [InlineData(LowPolyAbstractionQuality.High)]
    [InlineData(LowPolyAbstractionQuality.Ultra)]
    public void ThePaintIsNeverMoreOpaqueThanTheMaterial(LowPolyAbstractionQuality quality)
    {
        using var pipeline = CreatePipeline();
        const int size = 256;
        var source = new int[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var distance = Math.Sqrt((x - 128) * (x - 128) + (y - 128) * (y - 128));
                var alpha = (int)Math.Round(Math.Clamp((100 - distance) / 24, 0, 1) * 255);
                source[y * size + x] = alpha << 24 | alpha * (40 + x * 3 / 4) / 255 << 16 | alpha * (200 - y / 2) / 255 << 8 | alpha * (80 + (x + y) / 4) / 255;
            }
        }

        var rendering = Render(pipeline, source, size, size, Parameters(quality));

        for (var index = 0; index < source.Length; index++)
            Assert.True(Alpha(rendering[index]) <= Alpha(source[index]), $"({index % size}, {index / size}) {Alpha(rendering[index])} > {Alpha(source[index])}");
    }

    [Fact]
    public void TheHollowsStayTransparentWhenTheMaterialIsReducedForTheCalculation()
    {
        using var pipeline = CreatePipeline();
        const int scale = 8, size = 256 * scale;
        var hollow = HollowShape(Hollow.Notch);
        bool Inside(int x, int y) => hollow(x / scale, y / scale);

        var rendering = Render(pipeline, Silhouette(size, Inside), size, size, Parameters(LowPolyAbstractionQuality.Balanced));

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (Alpha(rendering[y * size + x]) > 0)
                    Assert.True(AnyWithin(x, y, 3, Inside), $"({x}, {y})");
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
        Assert.True(pipeline.TryGetVisibleBounds(192, 192, out var rect));
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

    [Theory]
    [InlineData(192, 64, 64, 48, 40)]
    [InlineData(192, 61, 70, 45, 37)]
    [InlineData(192, 2, 3, 40, 41)]
    [InlineData(192, 150, 149, 42, 43)]
    [InlineData(190, 148, 147, 42, 43)]
    public void TheVisibleBoundsHugTheShapeAndItsPaddingOnAFourPixelGrid(int canvas, int left, int top, int width, int height)
    {
        using var pipeline = CreatePipeline();
        var padding = LowPolyAbstractionSettings.MarginPadding;
        var parameters = Parameters(seed: 5);
        using var source = GraphicsDevice.GetDefault().AllocateReadWriteTexture2D<Bgra32, Float4>(canvas, canvas);
        Upload(source, TwoTone(canvas, canvas, left, top, width, height));

        pipeline.Simulate(source, canvas, canvas, 0, 0, canvas, canvas, in parameters);

        Assert.True(pipeline.TryGetVisibleBounds(canvas, canvas, out var rect));
        Assert.Equal((0, 0), (rect.X % 4, rect.Y % 4));
        Assert.True(rect.Width % 4 == 0 || rect.X + rect.Width == canvas, $"{rect}");
        Assert.True(rect.Height % 4 == 0 || rect.Y + rect.Height == canvas, $"{rect}");
        Assert.InRange(rect.X, Math.Max(left - padding - 3, 0), Math.Max(left - padding, 0));
        Assert.InRange(rect.Y, Math.Max(top - padding - 3, 0), Math.Max(top - padding, 0));
        Assert.InRange(rect.X + rect.Width, Math.Min(left + width + padding, canvas), Math.Min(left + width + padding + 3, canvas));
        Assert.InRange(rect.Y + rect.Height, Math.Min(top + height + padding, canvas), Math.Min(top + height + padding + 3, canvas));
    }

    [Fact]
    public void ASourceThatTurnsTransparentHasNoVisibleBounds()
    {
        using var pipeline = CreatePipeline();
        var parameters = Parameters();
        using var source = UploadedSquare();
        pipeline.Simulate(source, 128, 128, 0, 0, 128, 128, in parameters);
        Assert.True(pipeline.TryGetVisibleBounds(128, 128, out _));

        Upload(source, new int[128 * 128]);
        pipeline.Simulate(source, 128, 128, 0, 0, 128, 128, in parameters);

        Assert.False(pipeline.TryGetVisibleBounds(128, 128, out var rect));
        Assert.Equal(default, rect);
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
