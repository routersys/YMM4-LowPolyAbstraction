using System.Runtime.InteropServices;
using ComputeWeave;

namespace LowPolyAbstraction;

internal sealed class LowPolyAbstractionPipeline : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly ReadWriteBuffer<int> _scratch;
    private readonly ReadBackBuffer<int> _scratchReadBack;
    private ReadWriteBuffer<Float4>? _color;
    private ReadWriteBuffer<float>? _luma;
    private ReadWriteBuffer<float>? _edgeMagnitude;
    private ReadWriteBuffer<float>? _weight;
    private ReadWriteBuffer<int>? _jumpFloodA;
    private ReadWriteBuffer<int>? _jumpFloodB;
    private ReadWriteBuffer<int>? _assignment;
    private ReadWriteBuffer<int>? _counts;
    private ReadWriteBuffer<int>? _blockSums;
    private ReadWriteBuffer<int>? _cellBest;
    private ReadWriteBuffer<Float2>? _sitePositions;
    private ReadWriteBuffer<int>? _siteKinds;
    private ReadWriteBuffer<int>? _siteAccumulators;
    private ReadWriteBuffer<int>? _siteColorAccumulators;
    private ReadWriteBuffer<Float4>? _siteColors;
    private ReadWriteBuffer<int>? _incidenceCounts;
    private ReadWriteBuffer<int>? _incidence;
    private ReadWriteBuffer<int>? _triangleVertices;
    private ReadWriteBuffer<int>? _triangleAccumulatorsA;
    private ReadWriteBuffer<int>? _triangleAccumulatorsB;
    private ReadWriteBuffer<Float4>? _triangleColors;
    private ReadWriteBuffer<float>? _triangleErrors;
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

    private LowPolyAbstractionPipeline(GraphicsDevice device)
    {
        _device = device;
        _scratch = device.AllocateReadWriteBuffer<int>(LowPolyAbstractionSettings.ScratchLength);
        _scratchReadBack = device.AllocateReadBackBuffer<int>(LowPolyAbstractionSettings.ScratchLength);
    }

    public static LowPolyAbstractionPipeline? TryCreate()
    {
        try
        {
            return new LowPolyAbstractionPipeline(GraphicsDevice.GetDefault());
        }
        catch
        {
            return null;
        }
    }

    public static LowPolyAbstractionPipeline? TryCreate(GraphicsDevice device)
    {
        try
        {
            return new LowPolyAbstractionPipeline(device);
        }
        catch
        {
            return null;
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
        using (ComputeContext context = _device.CreateComputeContext())
            RecordFullPipeline(in context, sourceTexture, outputTexture, width, height, in parameters);
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
        using ComputeContext context = _device.CreateComputeContext();
        RecordFullPipeline(in context, source, destination, width, height, in parameters);
        context.Submit();
    }

    internal void ProcessSharedAndWait(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> destination,
        int width,
        int height,
        in Parameters parameters)
    {
        EnsureGridFor(width, height, parameters.Quality);
        using ComputeContext context = _device.CreateComputeContext();
        RecordFullPipeline(in context, source, destination, width, height, in parameters);
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
        EnsureGridFor(canvasWidth, canvasHeight, parameters.Quality);
        var derived = Derive(canvasWidth, canvasHeight, in parameters);
        using (ComputeContext context = _device.CreateComputeContext())
            RecordAnalyzeStage(in context, source, sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight, in derived);
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
        {
            using ComputeContext context = _device.CreateComputeContext();
            RecordStructureStage(in context, in derived, in parameters);
        }
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
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> output,
        int canvasWidth,
        int canvasHeight,
        int sourceOffsetX,
        int sourceOffsetY,
        int sourceWidth,
        int sourceHeight,
        PixelRect rect,
        in Parameters parameters)
    {
        var derived = Derive(canvasWidth, canvasHeight, in parameters);
        using ComputeContext context = _device.CreateComputeContext();
        RecordRenderStage(in context, output, rect, in derived, in parameters);
    }

    private void RecordFullPipeline(
        in ComputeContext context,
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> output,
        int width,
        int height,
        in Parameters parameters)
    {
        _structureKey = null;
        var derived = Derive(width, height, in parameters);
        RecordAnalyzeStage(in context, source, 0, 0, width, height, in derived);
        RecordStructureStage(in context, in derived, in parameters);
        _hasStructure = true;
        RecordRenderStage(in context, output, new PixelRect(0, 0, width, height), in derived, in parameters);
    }

    private void RecordAnalyzeStage(
        in ComputeContext context,
        ReadWriteTexture2D<Bgra32, Float4> source,
        int sourceOffsetX,
        int sourceOffsetY,
        int sourceWidth,
        int sourceHeight,
        in DerivedValues derived)
    {
        context.For(1, new InitScratchShader(_scratch));
        context.Barrier(_scratch);
        context.For(derived.WorkingWidth, derived.WorkingHeight, new AnalyzeShader(
            source, _color!, _luma!, _scratch,
            sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight,
            derived.WorkingWidth, derived.WorkingHeight, derived.Scale,
            LowPolyAbstractionSettings.AlphaThreshold));
        context.Barrier(_color!);
        context.Barrier(_luma!);
        context.Barrier(_scratch);
    }

    private void RecordStructureStage(
        in ComputeContext context,
        in DerivedValues derived,
        in Parameters parameters)
    {
        var workingWidth = derived.WorkingWidth;
        var workingHeight = derived.WorkingHeight;
        var pixelCount = workingWidth * workingHeight;

        context.For(workingWidth, workingHeight, new EdgeShader(_luma!, _edgeMagnitude!, workingWidth, workingHeight));
        context.Barrier(_edgeMagnitude!);

        context.For(workingWidth, workingHeight, new EdgeDistanceSeedShader(
            _edgeMagnitude!, _jumpFloodA!, workingWidth, workingHeight, LowPolyAbstractionSettings.EdgeThreshold));
        context.Barrier(_jumpFloodA!);
        var reading = _jumpFloodA!;
        var writing = _jumpFloodB!;
        for (var stepSize = derived.JumpFloodInitialStep; stepSize >= 1; stepSize >>= 1)
        {
            context.For(workingWidth, workingHeight, new JumpFloodPixelPassShader(reading, writing, workingWidth, workingHeight, stepSize));
            context.Barrier(writing);
            (reading, writing) = (writing, reading);
        }
        context.For(workingWidth, workingHeight, new WeightShader(
            reading, _color!, _weight!, workingWidth, workingHeight,
            derived.LaneWidth, parameters.Fidelity, LowPolyAbstractionSettings.AlphaThreshold));
        context.Barrier(_weight!);

        var edgeCellCount = derived.EdgeCellCols * derived.EdgeCellRows;
        context.For(edgeCellCount, new FillIntShader(_cellBest!, edgeCellCount, -1));
        context.Barrier(_cellBest!);
        context.For(workingWidth, workingHeight, new EdgeBestShader(
            _edgeMagnitude!, _color!, _cellBest!, workingWidth, workingHeight,
            derived.EdgeCellCols, derived.EdgeSpacing, LowPolyAbstractionSettings.EdgeThreshold,
            LowPolyAbstractionSettings.AlphaThreshold));
        context.Barrier(_cellBest!);
        context.For(edgeCellCount, new CellCountShader(_cellBest!, _counts!, edgeCellCount));
        context.Barrier(_counts!);
        RecordScan(in context, edgeCellCount, derived.EdgeBudget, 0, LowPolyAbstractionSettings.ScratchPendingCount);
        context.For(BlockCount(edgeCellCount), new EdgeEmitShader(
            _cellBest!, _blockSums!, _scratch, _sitePositions!, _siteKinds!,
            edgeCellCount, BlockCount(edgeCellCount), derived.EdgeCellCols, derived.EdgeSpacing));
        context.Barrier(_sitePositions!);
        context.Barrier(_siteKinds!);
        context.For(1, new CommitSitesShader(_scratch));
        context.Barrier(_scratch);

        var interiorCellCount = derived.InteriorCellCols * derived.InteriorCellRows;
        context.For(interiorCellCount, new InteriorCountShader(
            _color!, _counts!, workingWidth, workingHeight,
            derived.InteriorCellCols, interiorCellCount, derived.InteriorSpacing,
            parameters.Seed, LowPolyAbstractionSettings.AlphaThreshold));
        context.Barrier(_counts!);
        RecordScan(in context, interiorCellCount, derived.TargetSites, 1, LowPolyAbstractionSettings.ScratchPendingCount);
        context.For(BlockCount(interiorCellCount), new InteriorEmitShader(
            _counts!, _blockSums!, _scratch, _sitePositions!, _siteKinds!,
            interiorCellCount, BlockCount(interiorCellCount), derived.InteriorCellCols, derived.InteriorSpacing,
            parameters.Seed));
        context.Barrier(_sitePositions!);
        context.Barrier(_siteKinds!);
        context.For(1, new CommitSitesShader(_scratch));
        context.Barrier(_scratch);

        RecordLloydIterations(in context, in derived, derived.CvtIterations);
        RecordVoronoi(in context, in derived);
        RecordTriangulation(in context, in derived, pixelCount);
        RecordTriangleColors(in context, in derived, false);

        for (var pass = 0; pass < derived.RefinePasses; pass++)
        {
            context.For(derived.TriangleCapacity, new RefineCountShader(
                _triangleErrors!, _scratch, _counts!, derived.TriangleCapacity, derived.ErrorThreshold));
            context.Barrier(_counts!);
            RecordScan(in context, derived.TriangleCapacity, derived.RefineSiteLimit, 1, LowPolyAbstractionSettings.ScratchPendingCount);
            context.For(BlockCount(derived.TriangleCapacity), new RefineEmitShader(
                _counts!, _blockSums!, _scratch, _triangleVertices!, _sitePositions!, _siteKinds!,
                derived.TriangleCapacity, BlockCount(derived.TriangleCapacity)));
            context.Barrier(_sitePositions!);
            context.Barrier(_siteKinds!);
            context.For(1, new CommitSitesShader(_scratch));
            context.Barrier(_scratch);

            RecordLloydIterations(in context, in derived, LowPolyAbstractionSettings.PostInsertLloydIterations);
            RecordVoronoi(in context, in derived);
            RecordTriangulation(in context, in derived, pixelCount);
            RecordTriangleColors(in context, in derived, pass == derived.RefinePasses - 1);
        }

        var siteColorLength = _siteCapacity * 10;
        context.For(siteColorLength, new FillIntShader(_siteColorAccumulators!, siteColorLength, 0));
        context.Barrier(_siteColorAccumulators!);
        context.For(workingWidth, workingHeight, new SiteColorAccumulateShader(
            _assignment!, _color!, _siteColorAccumulators!, workingWidth, workingHeight,
            LowPolyAbstractionSettings.AlphaThreshold));
        context.Barrier(_siteColorAccumulators!);
        context.For(_siteCapacity, new SiteColorFinalizeShader(_siteColorAccumulators!, _siteColors!, _siteCapacity));
        context.Barrier(_siteColors!);
    }

    private void RecordLloydIterations(in ComputeContext context, in DerivedValues derived, int iterations)
    {
        var accumulatorLength = _siteCapacity * 6;
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            RecordVoronoi(in context, in derived);
            context.For(accumulatorLength, new FillIntShader(_siteAccumulators!, accumulatorLength, 0));
            context.Barrier(_siteAccumulators!);
            context.For(derived.WorkingWidth, derived.WorkingHeight, new CentroidAccumulateShader(
                _assignment!, _weight!, _siteAccumulators!, derived.WorkingWidth, derived.WorkingHeight));
            context.Barrier(_siteAccumulators!);
            context.For(_siteCapacity, new UpdateSitesShader(
                _sitePositions!, _siteKinds!, _siteAccumulators!, _scratch,
                derived.WorkingWidth, derived.WorkingHeight, _siteCapacity));
            context.Barrier(_sitePositions!);
        }
    }

    private void RecordVoronoi(in ComputeContext context, in DerivedValues derived)
    {
        var workingWidth = derived.WorkingWidth;
        var workingHeight = derived.WorkingHeight;
        context.For(workingWidth, workingHeight, new VoronoiClearShader(_jumpFloodA!, workingWidth, workingHeight));
        context.Barrier(_jumpFloodA!);
        context.For(_siteCapacity, new VoronoiScatterShader(
            _sitePositions!, _scratch, _jumpFloodA!, workingWidth, workingHeight, _siteCapacity));
        context.Barrier(_jumpFloodA!);
        var reading = _jumpFloodA!;
        var writing = _jumpFloodB!;
        for (var stepSize = derived.JumpFloodInitialStep; stepSize >= 1; stepSize >>= 1)
        {
            context.For(workingWidth, workingHeight, new JumpFloodSitePassShader(
                reading, writing, _sitePositions!, workingWidth, workingHeight, stepSize));
            context.Barrier(writing);
            (reading, writing) = (writing, reading);
        }
        _assignment = reading;
    }

    private void RecordTriangulation(in ComputeContext context, in DerivedValues derived, int pixelCount)
    {
        context.For(derived.WorkingWidth, derived.WorkingHeight, new CornerCountShader(
            _assignment!, _counts!, derived.WorkingWidth, derived.WorkingHeight));
        context.Barrier(_counts!);
        RecordScan(in context, pixelCount, derived.TriangleCapacity, 0, LowPolyAbstractionSettings.ScratchTriangleCount);
        context.For(BlockCount(pixelCount), new TriangleEmitShader(
            _assignment!, _counts!, _blockSums!, _scratch, _triangleVertices!,
            derived.WorkingWidth, derived.WorkingHeight, BlockCount(pixelCount)));
        context.Barrier(_triangleVertices!);
        context.For(_siteCapacity, new FillIntShader(_incidenceCounts!, _siteCapacity, 0));
        context.Barrier(_incidenceCounts!);
        context.For(derived.TriangleCapacity, new IncidenceBuildShader(
            _triangleVertices!, _incidenceCounts!, _incidence!, _scratch, derived.TriangleCapacity));
        context.Barrier(_incidenceCounts!);
        context.Barrier(_incidence!);
        context.For(_siteCapacity, new SortIncidenceShader(_incidenceCounts!, _incidence!, _siteCapacity));
        context.Barrier(_incidence!);
    }

    private void RecordTriangleColors(in ComputeContext context, in DerivedValues derived, bool computeColors)
    {
        var lengthA = derived.TriangleCapacity * 14;
        context.For(lengthA, new FillIntShader(_triangleAccumulatorsA!, lengthA, 0));
        context.Barrier(_triangleAccumulatorsA!);
        context.For(derived.WorkingWidth, derived.WorkingHeight, new TriangleMapShader(
            _assignment!, _color!, _sitePositions!, _triangleVertices!, _incidenceCounts!, _incidence!, _counts!,
            derived.WorkingWidth, derived.WorkingHeight, LowPolyAbstractionSettings.AlphaThreshold));
        context.Barrier(_counts!);
        context.For(derived.WorkingWidth, derived.WorkingHeight, new TriangleColorPassShader(
            _counts!, _color!, _triangleAccumulatorsA!, _triangleAccumulatorsB!,
            derived.WorkingWidth, derived.WorkingHeight, 0, LowPolyAbstractionSettings.TrimSigmaFactor));
        context.Barrier(_triangleAccumulatorsA!);
        if (computeColors)
        {
            var lengthB = derived.TriangleCapacity * 10;
            context.For(lengthB, new FillIntShader(_triangleAccumulatorsB!, lengthB, 0));
            context.Barrier(_triangleAccumulatorsB!);
            context.For(derived.WorkingWidth, derived.WorkingHeight, new TriangleColorPassShader(
                _counts!, _color!, _triangleAccumulatorsA!, _triangleAccumulatorsB!,
                derived.WorkingWidth, derived.WorkingHeight, 1, LowPolyAbstractionSettings.TrimSigmaFactor));
            context.Barrier(_triangleAccumulatorsB!);
        }
        context.For(derived.TriangleCapacity, new FinalizeTrianglesShader(
            _triangleAccumulatorsA!, _triangleAccumulatorsB!, _scratch, _triangleColors!, _triangleErrors!,
            derived.TriangleCapacity, computeColors ? 1 : 0));
        if (computeColors)
            context.Barrier(_triangleColors!);
        context.Barrier(_triangleErrors!);
    }

    private void RecordScan(in ComputeContext context, int elementCount, int capLimit, int subtractSiteCount, int outputSlot)
    {
        var blocks = BlockCount(elementCount);
        context.For(blocks, new BlockCountShader(_counts!, _blockSums!, elementCount, blocks));
        context.Barrier(_blockSums!);
        context.For(1, new BlockPrefixShader(_blockSums!, _scratch, blocks, capLimit, subtractSiteCount, outputSlot));
        context.Barrier(_blockSums!);
        context.Barrier(_scratch);
    }

    private void RecordRenderStage(
        in ComputeContext context,
        ReadWriteTexture2D<Bgra32, Float4> output,
        PixelRect rect,
        in DerivedValues derived,
        in Parameters parameters)
    {
        context.For(rect.Width, rect.Height, new RenderShader(
            _assignment!, _sitePositions!, _triangleVertices!, _incidenceCounts!, _incidence!,
            _triangleColors!, _siteColors!, output,
            rect.X, rect.Y, rect.Width, rect.Height,
            derived.WorkingWidth, derived.WorkingHeight, derived.Scale,
            Math.Clamp(parameters.Gradient, 0f, 1f),
            Math.Clamp(parameters.Wireframe, 0f, 1f) * LowPolyAbstractionSettings.MaximumWireframeWidth,
            Math.Clamp(parameters.Wireframe, 0f, 1f) > 0f ? 1f : 0f,
            Math.Clamp(parameters.Saturation, 0f, 1f),
            Math.Clamp(parameters.Jitter, 0f, 1f),
            parameters.Seed));
    }

    private static int BlockCount(int elementCount)
        => (elementCount + LowPolyAbstractionSettings.ScanBlockSize - 1) / LowPolyAbstractionSettings.ScanBlockSize;

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

        DisposeGridBuffers();
        var pixelCount = workingWidth * workingHeight;
        var triangleCapacity = LowPolyAbstractionSettings.GetTriangleCapacity(siteCapacity);
        var scanLength = Math.Max((workingWidth + 1) * (workingHeight + 1), triangleCapacity);
        var cellBestLength = (workingWidth / 2 + 1) * (workingHeight / 2 + 1);
        _color = _device.AllocateReadWriteBuffer<Float4>(pixelCount);
        _luma = _device.AllocateReadWriteBuffer<float>(pixelCount);
        _edgeMagnitude = _device.AllocateReadWriteBuffer<float>(pixelCount);
        _weight = _device.AllocateReadWriteBuffer<float>(pixelCount);
        _jumpFloodA = _device.AllocateReadWriteBuffer<int>(pixelCount);
        _jumpFloodB = _device.AllocateReadWriteBuffer<int>(pixelCount);
        _counts = _device.AllocateReadWriteBuffer<int>(scanLength);
        _blockSums = _device.AllocateReadWriteBuffer<int>(BlockCount(scanLength) + 1);
        _cellBest = _device.AllocateReadWriteBuffer<int>(cellBestLength);
        _sitePositions = _device.AllocateReadWriteBuffer<Float2>(siteCapacity);
        _siteKinds = _device.AllocateReadWriteBuffer<int>(siteCapacity);
        _siteAccumulators = _device.AllocateReadWriteBuffer<int>(siteCapacity * 6);
        _siteColorAccumulators = _device.AllocateReadWriteBuffer<int>(siteCapacity * 10);
        _siteColors = _device.AllocateReadWriteBuffer<Float4>(siteCapacity);
        _incidenceCounts = _device.AllocateReadWriteBuffer<int>(siteCapacity);
        _incidence = _device.AllocateReadWriteBuffer<int>(siteCapacity * LowPolyAbstractionSettings.MaxIncidence);
        _triangleVertices = _device.AllocateReadWriteBuffer<int>(triangleCapacity * 3);
        _triangleAccumulatorsA = _device.AllocateReadWriteBuffer<int>(triangleCapacity * 14);
        _triangleAccumulatorsB = _device.AllocateReadWriteBuffer<int>(triangleCapacity * 10);
        _triangleColors = _device.AllocateReadWriteBuffer<Float4>(triangleCapacity);
        _triangleErrors = _device.AllocateReadWriteBuffer<float>(triangleCapacity);
        _assignment = null;
        _workingWidth = workingWidth;
        _workingHeight = workingHeight;
        _siteCapacity = siteCapacity;
        _structureKey = null;
        _hasStructure = false;
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

    private void DisposeGridBuffers()
    {
        _color?.Dispose();
        _luma?.Dispose();
        _edgeMagnitude?.Dispose();
        _weight?.Dispose();
        _jumpFloodA?.Dispose();
        _jumpFloodB?.Dispose();
        _counts?.Dispose();
        _blockSums?.Dispose();
        _cellBest?.Dispose();
        _sitePositions?.Dispose();
        _siteKinds?.Dispose();
        _siteAccumulators?.Dispose();
        _siteColorAccumulators?.Dispose();
        _siteColors?.Dispose();
        _incidenceCounts?.Dispose();
        _incidence?.Dispose();
        _triangleVertices?.Dispose();
        _triangleAccumulatorsA?.Dispose();
        _triangleAccumulatorsB?.Dispose();
        _triangleColors?.Dispose();
        _triangleErrors?.Dispose();
        _color = null;
        _luma = null;
        _edgeMagnitude = null;
        _weight = null;
        _jumpFloodA = null;
        _jumpFloodB = null;
        _assignment = null;
        _counts = null;
        _blockSums = null;
        _cellBest = null;
        _sitePositions = null;
        _siteKinds = null;
        _siteAccumulators = null;
        _siteColorAccumulators = null;
        _siteColors = null;
        _incidenceCounts = null;
        _incidence = null;
        _triangleVertices = null;
        _triangleAccumulatorsA = null;
        _triangleAccumulatorsB = null;
        _triangleColors = null;
        _triangleErrors = null;
        _structureKey = null;
        _hasStructure = false;
        _workingWidth = 0;
        _workingHeight = 0;
        _siteCapacity = 0;
    }

    public void Dispose()
    {
        DisposeGridBuffers();
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

    private readonly record struct DerivedValues(
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
