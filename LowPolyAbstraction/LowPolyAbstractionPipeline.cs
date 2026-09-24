using System.ComponentModel;
using System.Runtime.InteropServices;
using ComputeWeave;

namespace LowPolyAbstraction;

internal sealed class LowPolyAbstractionPipeline : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly LowPolyAbstractionPipelineHost _host;
    private readonly ReadWriteBuffer<int> _scratch;
    private readonly ReadBackBuffer<int> _scratchReadBack;
    private StructureKey? _structureKey;
    private int _workingWidth;
    private int _workingHeight;
    private int _siteCapacity;
    private int _cachedMinX;
    private int _cachedMinY;
    private int _cachedMaxX;
    private int _cachedMaxY;
    private bool _hasStructure;
    private ReadWriteTexture2D<Bgra32, Float4>? _packedSource;
    private ReadWriteTexture2D<Bgra32, Float4>? _packedOutput;
    private int _packedWidth;
    private int _packedHeight;

    private LowPolyAbstractionPipeline(GraphicsDevice device, LowPolyAbstractionPipelineHost host)
    {
        _device = device;
        _host = host;
        _scratch = device.AllocateReadWriteBuffer<int>(LowPolyAbstractionSettings.ScratchLength);
        _scratchReadBack = device.AllocateReadBackBuffer<int>(LowPolyAbstractionSettings.ScratchLength);
    }

    public static LowPolyAbstractionPipeline? TryCreate()
    {
        try
        {
            return TryCreate(GraphicsDevice.GetDefault());
        }
        catch
        {
            return null;
        }
    }

    public static LowPolyAbstractionPipeline? TryCreate(GraphicsDevice device)
    {
        LowPolyAbstractionPipelineHost? host = null;
        try
        {
            host = LowPolyAbstractionPipelineHost.Create(device, LowPolyAbstractionSettings.MaximumPendingSubmissions);
            var pipeline = new LowPolyAbstractionPipeline(device, host);
            host = null;
            return pipeline;
        }
        catch (Win32Exception)
        {
            return null;
        }
        finally
        {
            host?.Dispose();
            host?.WaitForDisposal();
        }
    }

    internal void WaitForCompletion()
    {
        _device.For(1, new FillIntShader(_scratch, 0, 0));
    }

    public void Process(ReadOnlySpan<int> source, Span<int> destination, int width, int height, in Parameters parameters)
    {
        var pixelCount = checked(width * height);
        EnsureGridFor(width, height, parameters.Quality);
        EnsurePackedTextures(width, height);
        var sourceTexture = _packedSource!;
        var outputTexture = _packedOutput!;
        sourceTexture.CopyFrom(MemoryMarshal.Cast<int, Bgra32>(source[..pixelCount]));
        SubmitFullPipeline(sourceTexture, outputTexture, width, height, in parameters).Wait();
        outputTexture.CopyTo(MemoryMarshal.Cast<int, Bgra32>(destination[..pixelCount]));
    }

    public void Process(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> destination,
        int width,
        int height,
        in Parameters parameters)
    {
        EnsureGridFor(width, height, parameters.Quality);
        _ = SubmitFullPipeline(source, destination, width, height, in parameters);
    }

    internal void ProcessSharedAndWait(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> destination,
        int width,
        int height,
        in Parameters parameters)
    {
        EnsureGridFor(width, height, parameters.Quality);
        SubmitFullPipeline(source, destination, width, height, in parameters).Wait();
    }

    internal bool Simulate(
        ReadWriteTexture2D<Bgra32, Float4> source,
        int canvasWidth,
        int canvasHeight,
        int sourceOffsetX,
        int sourceOffsetY,
        int sourceWidth,
        int sourceHeight,
        in Parameters parameters)
    {
        var derived = BeginSimulate(canvasWidth, canvasHeight, in parameters);
        _host.RecordAnalyze(source, _scratch, sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight, in derived).Wait();
        return CompleteSimulate(canvasWidth, canvasHeight, in parameters, in derived);
    }

    internal bool Simulate(
        ComputeResourceBinding<ReadWriteTexture2D<Bgra32, Float4>> source,
        int canvasWidth,
        int canvasHeight,
        int sourceOffsetX,
        int sourceOffsetY,
        int sourceWidth,
        int sourceHeight,
        in Parameters parameters)
    {
        var derived = BeginSimulate(canvasWidth, canvasHeight, in parameters);
        _host.RecordSharedAnalyze(source, _scratch, sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight, in derived).Wait();
        return CompleteSimulate(canvasWidth, canvasHeight, in parameters, in derived);
    }

    private DerivedValues BeginSimulate(int canvasWidth, int canvasHeight, in Parameters parameters)
    {
        EnsureGridFor(canvasWidth, canvasHeight, parameters.Quality);
        return Derive(canvasWidth, canvasHeight, in parameters);
    }

    private bool CompleteSimulate(int canvasWidth, int canvasHeight, in Parameters parameters, in DerivedValues derived)
    {
        _scratchReadBack.CopyFrom(_scratch);
        var hashed = _scratchReadBack.Span;
        _cachedMinX = hashed[LowPolyAbstractionSettings.ScratchBoundsMinX];
        _cachedMinY = hashed[LowPolyAbstractionSettings.ScratchBoundsMinY];
        _cachedMaxX = hashed[LowPolyAbstractionSettings.ScratchBoundsMaxX];
        _cachedMaxY = hashed[LowPolyAbstractionSettings.ScratchBoundsMaxY];
        var key = new StructureKey(
            hashed[LowPolyAbstractionSettings.ScratchMaskHashSum],
            hashed[LowPolyAbstractionSettings.ScratchMaskHashMix],
            canvasWidth,
            canvasHeight,
            parameters.Quality,
            parameters.Seed,
            parameters.Detail,
            parameters.Fidelity,
            parameters.Refine);
        if (_structureKey == key)
            return false;

        if (_cachedMinX <= _cachedMaxX && _cachedMinY <= _cachedMaxY)
            _host.RecordStructure(_scratch, _siteCapacity, in derived, in parameters).Wait();
        _hasStructure = _cachedMinX <= _cachedMaxX && _cachedMinY <= _cachedMaxY;
        _structureKey = key;
        return true;
    }

    internal bool TryGetVisibleBounds(int canvasWidth, int canvasHeight, in Parameters parameters, out PixelRect rect)
    {
        rect = default;
        if (!_hasStructure || _cachedMinX > _cachedMaxX || _cachedMinY > _cachedMaxY)
            return false;

        var padding = LowPolyAbstractionSettings.MarginPadding;
        var left = Math.Clamp((_cachedMinX - padding) & ~3, 0, canvasWidth);
        var top = Math.Clamp((_cachedMinY - padding) & ~3, 0, canvasHeight);
        var right = Math.Clamp(_cachedMaxX + padding, 0, canvasWidth);
        var bottom = Math.Clamp(_cachedMaxY + padding, 0, canvasHeight);
        var width = Math.Min((right - left + 3) & ~3, canvasWidth - left);
        var height = Math.Min((bottom - top + 3) & ~3, canvasHeight - top);
        if (width <= 0 || height <= 0)
            return false;

        rect = new PixelRect(left, top, width, height);
        return true;
    }

    internal void RenderVisible(
        ReadWriteTexture2D<Bgra32, Float4> output,
        int canvasWidth,
        int canvasHeight,
        PixelRect rect,
        in Parameters parameters)
    {
        var derived = Derive(canvasWidth, canvasHeight, in parameters);
        _host.RecordRender(output, in rect, in derived, in parameters).Wait();
    }

    internal void RenderVisible(
        ComputeResourceBinding<ReadWriteTexture2D<Bgra32, Float4>> output,
        int canvasWidth,
        int canvasHeight,
        PixelRect rect,
        in Parameters parameters)
    {
        var derived = Derive(canvasWidth, canvasHeight, in parameters);
        _host.RecordSharedRender(output, in rect, in derived, in parameters).Wait();
    }

    private ComputeSubmission SubmitFullPipeline(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> output,
        int width,
        int height,
        in Parameters parameters)
    {
        _structureKey = null;
        var derived = Derive(width, height, in parameters);
        var submission = _host.RecordFullPipeline(source, output, _scratch, width, height, _siteCapacity, in derived, in parameters);
        _hasStructure = true;
        return submission;
    }

    private DerivedValues Derive(int width, int height, in Parameters parameters)
    {
        var settings = LowPolyAbstractionSettings.GetQuality(parameters.Quality);
        var (workingWidth, workingHeight, scale) = LowPolyAbstractionSettings.GetWorkingSize(width, height, settings.WorkingResolution);
        var targetSites = Math.Min(
            LowPolyAbstractionSettings.GetSiteCount(parameters.Detail, settings.BaseSites),
            LowPolyAbstractionSettings.GetSiteCapacity(settings.BaseSites));
        var edgeBudget = Math.Max((int)(targetSites * LowPolyAbstractionSettings.EdgeSiteBudgetFraction), 8);
        var edgeSpacing = LowPolyAbstractionSettings.GetEdgeSampleSpacing(workingWidth, workingHeight, parameters.Fidelity);
        var interiorTarget = Math.Max((int)(targetSites * (1f - LowPolyAbstractionSettings.EdgeSiteBudgetFraction)), 64);
        var interiorSpacing = Math.Max(MathF.Sqrt(workingWidth * (float)workingHeight / interiorTarget), 1f);
        var siteCapacity = LowPolyAbstractionSettings.GetSiteCapacity(settings.BaseSites);
        return new DerivedValues(
            workingWidth,
            workingHeight,
            scale,
            targetSites,
            edgeBudget,
            edgeSpacing,
            (int)(workingWidth / edgeSpacing) + 1,
            (int)(workingHeight / edgeSpacing) + 1,
            interiorSpacing,
            (int)(workingWidth / interiorSpacing) + 1,
            (int)(workingHeight / interiorSpacing) + 1,
            LowPolyAbstractionSettings.GetLaneWidth(workingWidth, workingHeight),
            LowPolyAbstractionSettings.GetErrorThreshold(parameters.Refine),
            settings.CvtIterations,
            settings.RefinePasses,
            LowPolyAbstractionSettings.GetTriangleCapacity(siteCapacity),
            LowPolyAbstractionSettings.GetJumpFloodInitialStep(workingWidth, workingHeight),
            Math.Min((int)(targetSites * LowPolyAbstractionSettings.SiteCapacityFraction), siteCapacity));
    }

    private void EnsureGridFor(int width, int height, LowPolyAbstractionQuality quality)
    {
        var settings = LowPolyAbstractionSettings.GetQuality(quality);
        var (workingWidth, workingHeight, _) = LowPolyAbstractionSettings.GetWorkingSize(width, height, settings.WorkingResolution);
        EnsureGrid(workingWidth, workingHeight, LowPolyAbstractionSettings.GetSiteCapacity(settings.BaseSites));
    }

    private void EnsureGrid(int workingWidth, int workingHeight, int siteCapacity)
    {
        if (_workingWidth == workingWidth && _workingHeight == workingHeight && _siteCapacity == siteCapacity)
            return;

        _structureKey = null;
        _hasStructure = false;
        _workingWidth = 0;
        _workingHeight = 0;
        _siteCapacity = 0;
        var pixelCount = workingWidth * workingHeight;
        var triangleCapacity = LowPolyAbstractionSettings.GetTriangleCapacity(siteCapacity);
        var scanLength = Math.Max((workingWidth + 1) * (workingHeight + 1), triangleCapacity);
        var cellBestLength = (workingWidth / 2 + 1) * (workingHeight / 2 + 1);
        if (!_host.TryEnsureGrid(
                new LowPolyAbstractionGridResources.Plan(
                    blockSumsLength: LowPolyAbstractionPipelineHost.BlockCount(scanLength) + 1,
                    cellBestLength: cellBestLength,
                    colorLength: pixelCount,
                    countsLength: scanLength,
                    edgeMagnitudeLength: pixelCount,
                    incidenceLength: siteCapacity * LowPolyAbstractionSettings.MaxIncidence,
                    incidenceCountsLength: siteCapacity,
                    jumpFloodALength: pixelCount,
                    jumpFloodBLength: pixelCount,
                    lumaLength: pixelCount,
                    siteAccumulatorsLength: siteCapacity * 6,
                    siteColorAccumulatorsLength: siteCapacity * 10,
                    siteColorsLength: siteCapacity,
                    siteKindsLength: siteCapacity,
                    sitePositionsLength: siteCapacity,
                    triangleAccumulatorsALength: triangleCapacity * 16,
                    triangleAccumulatorsBLength: triangleCapacity * 10,
                    triangleColorsLength: triangleCapacity,
                    triangleErrorsLength: triangleCapacity,
                    triangleVerticesLength: triangleCapacity * 3,
                    weightLength: pixelCount),
                out _))
            throw new InvalidOperationException();
        _workingWidth = workingWidth;
        _workingHeight = workingHeight;
        _siteCapacity = siteCapacity;
    }

    private void EnsurePackedTextures(int width, int height)
    {
        if (_packedWidth == width && _packedHeight == height)
            return;

        _packedSource?.Dispose();
        _packedOutput?.Dispose();
        _packedSource = _device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        _packedOutput = _device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        _packedWidth = width;
        _packedHeight = height;
    }

    public void Dispose()
    {
        _host.Dispose();
        _host.WaitForDisposal();
        _packedSource?.Dispose();
        _packedOutput?.Dispose();
        _packedSource = null;
        _packedOutput = null;
        _packedWidth = 0;
        _packedHeight = 0;
        _scratchReadBack.Dispose();
        _scratch.Dispose();
    }

    internal readonly record struct PixelRect(int X, int Y, int Width, int Height);

    private readonly record struct StructureKey(
        int MaskHashSum,
        int MaskHashMix,
        int CanvasWidth,
        int CanvasHeight,
        LowPolyAbstractionQuality Quality,
        int Seed,
        float Detail,
        float Fidelity,
        float Refine);

    internal readonly record struct DerivedValues(
        int WorkingWidth,
        int WorkingHeight,
        float Scale,
        int TargetSites,
        int EdgeBudget,
        float EdgeSpacing,
        int EdgeCellCols,
        int EdgeCellRows,
        float InteriorSpacing,
        int InteriorCellCols,
        int InteriorCellRows,
        float LaneWidth,
        float ErrorThreshold,
        int CvtIterations,
        int RefinePasses,
        int TriangleCapacity,
        int JumpFloodInitialStep,
        int RefineSiteLimit);

    internal readonly record struct Parameters(
        LowPolyAbstractionQuality Quality,
        float Detail,
        float Fidelity,
        float Refine,
        float Gradient,
        float Wireframe,
        float Saturation,
        float Jitter,
        int Seed);
}
