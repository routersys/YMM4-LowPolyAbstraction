using ComputeWeave;

namespace LowPolyAbstraction;

[ComputeResourceGroup]
internal sealed partial class LowPolyAbstractionGridResources
{
    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<Float4> Color { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> Luma { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> EdgeMagnitude { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> Weight { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<int> JumpFloodA { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<int> JumpFloodB { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<int> Counts { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<int> BlockSums { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<int> CellBest { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<Float2> SitePositions { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<int> SiteKinds { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<int> SiteAccumulators { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<int> SiteColorAccumulators { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<Float4> SiteColors { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<int> IncidenceCounts { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<int> Incidence { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<int> TriangleVertices { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<int> TriangleAccumulatorsA { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<int> TriangleAccumulatorsB { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<Float4> TriangleColors { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> TriangleErrors { get; }
}

[ComputePipelineHost("_device", 1)]
internal sealed partial class LowPolyAbstractionPipelineHost
{
    private readonly GraphicsDevice _device;

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite, ComputeResourceRecovery.Recompute)]
    private readonly ComputeResourceGroupSlot<LowPolyAbstractionGridResources> _grid = new();

    internal static int BlockCount(int elementCount)
        => (elementCount + LowPolyAbstractionSettings.ScanBlockSize - 1) / LowPolyAbstractionSettings.ScanBlockSize;

    [ComputePipeline]
    private void RecordFullPipeline(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(_grid))] LowPolyAbstractionGridResources grid,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteTexture2D<Bgra32, Float4> source,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteTexture2D<Bgra32, Float4> output,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> scratch,
        int width,
        int height,
        int siteCapacity,
        in LowPolyAbstractionPipeline.DerivedValues derived,
        in LowPolyAbstractionPipeline.Parameters parameters)
    {
        _ = _device;

        RecordAnalyzeStage(in context, grid, source, scratch, 0, 0, width, height, in derived);
        RecordStructureStage(in context, grid, scratch, siteCapacity, in derived, in parameters);
        RecordRenderStage(in context, grid, output, new LowPolyAbstractionPipeline.PixelRect(0, 0, width, height), in derived, in parameters);
    }

    [ComputePipeline]
    private void RecordAnalyze(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(_grid))] LowPolyAbstractionGridResources grid,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteTexture2D<Bgra32, Float4> source,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> scratch,
        int sourceOffsetX,
        int sourceOffsetY,
        int sourceWidth,
        int sourceHeight,
        in LowPolyAbstractionPipeline.DerivedValues derived)
    {
        _ = _device;

        RecordAnalyzeStage(in context, grid, source, scratch, sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight, in derived);
    }

    [ComputePipeline]
    private void RecordStructure(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(_grid))] LowPolyAbstractionGridResources grid,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> scratch,
        int siteCapacity,
        in LowPolyAbstractionPipeline.DerivedValues derived,
        in LowPolyAbstractionPipeline.Parameters parameters)
    {
        _ = _device;

        RecordStructureStage(in context, grid, scratch, siteCapacity, in derived, in parameters);
    }

    [ComputePipeline]
    private void RecordRender(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(_grid))] LowPolyAbstractionGridResources grid,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteTexture2D<Bgra32, Float4> output,
        in LowPolyAbstractionPipeline.PixelRect rect,
        in LowPolyAbstractionPipeline.DerivedValues derived,
        in LowPolyAbstractionPipeline.Parameters parameters)
    {
        _ = _device;

        RecordRenderStage(in context, grid, output, rect, in derived, in parameters);
    }

    private static void RecordAnalyzeStage(
        in ComputeContext context,
        LowPolyAbstractionGridResources grid,
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteBuffer<int> scratch,
        int sourceOffsetX,
        int sourceOffsetY,
        int sourceWidth,
        int sourceHeight,
        in LowPolyAbstractionPipeline.DerivedValues derived)
    {
        context.For(1, new InitScratchShader(scratch));
        context.Barrier(scratch);
        context.For(derived.WorkingWidth, derived.WorkingHeight, new AnalyzeShader(
            source, grid.Color, grid.Luma, scratch,
            sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight,
            derived.WorkingWidth, derived.WorkingHeight, derived.Scale,
            LowPolyAbstractionSettings.AlphaThreshold));
        context.Barrier(grid.Color);
        context.Barrier(grid.Luma);
        context.Barrier(scratch);
    }

    private static void RecordStructureStage(
        in ComputeContext context,
        LowPolyAbstractionGridResources grid,
        ReadWriteBuffer<int> scratch,
        int siteCapacity,
        in LowPolyAbstractionPipeline.DerivedValues derived,
        in LowPolyAbstractionPipeline.Parameters parameters)
    {
        var workingWidth = derived.WorkingWidth;
        var workingHeight = derived.WorkingHeight;
        var pixelCount = workingWidth * workingHeight;

        context.For(workingWidth, workingHeight, new EdgeShader(grid.Luma, grid.EdgeMagnitude, workingWidth, workingHeight));
        context.Barrier(grid.EdgeMagnitude);

        context.For(workingWidth, workingHeight, new EdgeDistanceSeedShader(
            grid.EdgeMagnitude, grid.JumpFloodA, workingWidth, workingHeight, LowPolyAbstractionSettings.EdgeThreshold));
        context.Barrier(grid.JumpFloodA);
        var reading = grid.JumpFloodA;
        var writing = grid.JumpFloodB;
        for (var stepSize = derived.JumpFloodInitialStep; stepSize >= 1; stepSize >>= 1)
        {
            context.For(workingWidth, workingHeight, new JumpFloodPixelPassShader(reading, writing, workingWidth, workingHeight, stepSize));
            context.Barrier(writing);
            (reading, writing) = (writing, reading);
        }
        context.For(workingWidth, workingHeight, new WeightShader(
            reading, grid.Color, grid.Weight, workingWidth, workingHeight,
            derived.LaneWidth, parameters.Fidelity, LowPolyAbstractionSettings.AlphaThreshold));
        context.Barrier(grid.Weight);

        var edgeCellCount = derived.EdgeCellCols * derived.EdgeCellRows;
        context.For(edgeCellCount, new FillIntShader(grid.CellBest, edgeCellCount, -1));
        context.Barrier(grid.CellBest);
        context.For(workingWidth, workingHeight, new EdgeBestShader(
            grid.EdgeMagnitude, grid.Color, grid.CellBest, workingWidth, workingHeight,
            derived.EdgeCellCols, derived.EdgeSpacing, LowPolyAbstractionSettings.EdgeThreshold,
            LowPolyAbstractionSettings.AlphaThreshold));
        context.Barrier(grid.CellBest);
        context.For(edgeCellCount, new CellCountShader(grid.CellBest, grid.Counts, edgeCellCount));
        context.Barrier(grid.Counts);
        RecordScan(in context, grid, scratch, edgeCellCount, derived.EdgeBudget, 0, LowPolyAbstractionSettings.ScratchPendingCount);
        context.For(BlockCount(edgeCellCount), new EdgeEmitShader(
            grid.CellBest, grid.BlockSums, scratch, grid.SitePositions, grid.SiteKinds,
            edgeCellCount, BlockCount(edgeCellCount), derived.EdgeCellCols, derived.EdgeSpacing));
        context.Barrier(grid.SitePositions);
        context.Barrier(grid.SiteKinds);
        context.For(1, new CommitSitesShader(scratch));
        context.Barrier(scratch);

        var interiorCellCount = derived.InteriorCellCols * derived.InteriorCellRows;
        context.For(interiorCellCount, new InteriorCountShader(
            grid.Color, grid.Counts, workingWidth, workingHeight,
            derived.InteriorCellCols, interiorCellCount, derived.InteriorSpacing,
            parameters.Seed, LowPolyAbstractionSettings.AlphaThreshold));
        context.Barrier(grid.Counts);
        RecordScan(in context, grid, scratch, interiorCellCount, derived.TargetSites, 1, LowPolyAbstractionSettings.ScratchPendingCount);
        context.For(BlockCount(interiorCellCount), new InteriorEmitShader(
            grid.Counts, grid.BlockSums, scratch, grid.SitePositions, grid.SiteKinds,
            interiorCellCount, BlockCount(interiorCellCount), derived.InteriorCellCols, derived.InteriorSpacing,
            parameters.Seed));
        context.Barrier(grid.SitePositions);
        context.Barrier(grid.SiteKinds);
        context.For(1, new CommitSitesShader(scratch));
        context.Barrier(scratch);

        RecordLloydIterations(in context, grid, scratch, siteCapacity, in derived, derived.CvtIterations);
        RecordVoronoi(in context, grid, scratch, siteCapacity, in derived);
        RecordTriangulation(in context, grid, scratch, siteCapacity, in derived, pixelCount);
        RecordTriangleColors(in context, grid, scratch, in derived, false);

        for (var pass = 0; pass < derived.RefinePasses; pass++)
        {
            context.For(derived.TriangleCapacity, new RefineCountShader(
                grid.TriangleErrors, scratch, grid.Counts, derived.TriangleCapacity, derived.ErrorThreshold));
            context.Barrier(grid.Counts);
            RecordScan(in context, grid, scratch, derived.TriangleCapacity, derived.RefineSiteLimit, 1, LowPolyAbstractionSettings.ScratchPendingCount);
            context.For(BlockCount(derived.TriangleCapacity), new RefineEmitShader(
                grid.Counts, grid.BlockSums, scratch, grid.TriangleVertices, grid.SitePositions, grid.SiteKinds,
                derived.TriangleCapacity, BlockCount(derived.TriangleCapacity)));
            context.Barrier(grid.SitePositions);
            context.Barrier(grid.SiteKinds);
            context.For(1, new CommitSitesShader(scratch));
            context.Barrier(scratch);

            RecordLloydIterations(in context, grid, scratch, siteCapacity, in derived, LowPolyAbstractionSettings.PostInsertLloydIterations);
            RecordVoronoi(in context, grid, scratch, siteCapacity, in derived);
            RecordTriangulation(in context, grid, scratch, siteCapacity, in derived, pixelCount);
            RecordTriangleColors(in context, grid, scratch, in derived, pass == derived.RefinePasses - 1);
        }

        var siteColorLength = siteCapacity * 10;
        context.For(siteColorLength, new FillIntShader(grid.SiteColorAccumulators, siteColorLength, 0));
        context.Barrier(grid.SiteColorAccumulators);
        context.For(workingWidth, workingHeight, new SiteColorAccumulateShader(
            GetAssignment(grid, in derived), grid.Color, grid.SiteColorAccumulators, workingWidth, workingHeight,
            LowPolyAbstractionSettings.AlphaThreshold));
        context.Barrier(grid.SiteColorAccumulators);
        context.For(siteCapacity, new SiteColorFinalizeShader(grid.SiteColorAccumulators, grid.SiteColors, siteCapacity));
        context.Barrier(grid.SiteColors);
    }

    private static void RecordLloydIterations(
        in ComputeContext context,
        LowPolyAbstractionGridResources grid,
        ReadWriteBuffer<int> scratch,
        int siteCapacity,
        in LowPolyAbstractionPipeline.DerivedValues derived,
        int iterations)
    {
        var accumulatorLength = siteCapacity * 6;
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            RecordVoronoi(in context, grid, scratch, siteCapacity, in derived);
            context.For(accumulatorLength, new FillIntShader(grid.SiteAccumulators, accumulatorLength, 0));
            context.Barrier(grid.SiteAccumulators);
            context.For(derived.WorkingWidth, derived.WorkingHeight, new CentroidAccumulateShader(
                GetAssignment(grid, in derived), grid.Weight, grid.SiteAccumulators, derived.WorkingWidth, derived.WorkingHeight));
            context.Barrier(grid.SiteAccumulators);
            context.For(siteCapacity, new UpdateSitesShader(
                grid.SitePositions, grid.SiteKinds, grid.SiteAccumulators, scratch,
                derived.WorkingWidth, derived.WorkingHeight, siteCapacity));
            context.Barrier(grid.SitePositions);
        }
    }

    private static void RecordVoronoi(
        in ComputeContext context,
        LowPolyAbstractionGridResources grid,
        ReadWriteBuffer<int> scratch,
        int siteCapacity,
        in LowPolyAbstractionPipeline.DerivedValues derived)
    {
        var workingWidth = derived.WorkingWidth;
        var workingHeight = derived.WorkingHeight;
        context.For(workingWidth, workingHeight, new VoronoiClearShader(grid.JumpFloodA, workingWidth, workingHeight));
        context.Barrier(grid.JumpFloodA);
        context.For(siteCapacity, new VoronoiScatterShader(
            grid.SitePositions, scratch, grid.JumpFloodA, workingWidth, workingHeight, siteCapacity));
        context.Barrier(grid.JumpFloodA);
        var reading = grid.JumpFloodA;
        var writing = grid.JumpFloodB;
        for (var stepSize = derived.JumpFloodInitialStep; stepSize >= 1; stepSize >>= 1)
        {
            context.For(workingWidth, workingHeight, new JumpFloodSitePassShader(
                reading, writing, grid.SitePositions, workingWidth, workingHeight, stepSize));
            context.Barrier(writing);
            (reading, writing) = (writing, reading);
        }
    }

    private static ReadWriteBuffer<int> GetAssignment(LowPolyAbstractionGridResources grid, in LowPolyAbstractionPipeline.DerivedValues derived)
    {
        var passes = 0;
        for (var stepSize = derived.JumpFloodInitialStep; stepSize >= 1; stepSize >>= 1)
            passes++;
        return (passes & 1) == 0 ? grid.JumpFloodA : grid.JumpFloodB;
    }

    private static void RecordTriangulation(
        in ComputeContext context,
        LowPolyAbstractionGridResources grid,
        ReadWriteBuffer<int> scratch,
        int siteCapacity,
        in LowPolyAbstractionPipeline.DerivedValues derived,
        int pixelCount)
    {
        var assignment = GetAssignment(grid, in derived);
        context.For(derived.WorkingWidth, derived.WorkingHeight, new CornerCountShader(
            assignment, grid.Counts, derived.WorkingWidth, derived.WorkingHeight));
        context.Barrier(grid.Counts);
        RecordScan(in context, grid, scratch, pixelCount, derived.TriangleCapacity, 0, LowPolyAbstractionSettings.ScratchTriangleCount);
        context.For(BlockCount(pixelCount), new TriangleEmitShader(
            assignment, grid.Counts, grid.BlockSums, scratch, grid.TriangleVertices,
            derived.WorkingWidth, derived.WorkingHeight, BlockCount(pixelCount)));
        context.Barrier(grid.TriangleVertices);
        context.For(siteCapacity, new FillIntShader(grid.IncidenceCounts, siteCapacity, 0));
        context.Barrier(grid.IncidenceCounts);
        context.For(derived.TriangleCapacity, new IncidenceBuildShader(
            grid.TriangleVertices, grid.IncidenceCounts, grid.Incidence, scratch, derived.TriangleCapacity));
        context.Barrier(grid.IncidenceCounts);
        context.Barrier(grid.Incidence);
        context.For(siteCapacity, new SortIncidenceShader(grid.IncidenceCounts, grid.Incidence, siteCapacity));
        context.Barrier(grid.Incidence);
    }

    private static void RecordTriangleColors(
        in ComputeContext context,
        LowPolyAbstractionGridResources grid,
        ReadWriteBuffer<int> scratch,
        in LowPolyAbstractionPipeline.DerivedValues derived,
        bool computeColors)
    {
        var lengthA = derived.TriangleCapacity * 14;
        context.For(lengthA, new FillIntShader(grid.TriangleAccumulatorsA, lengthA, 0));
        context.Barrier(grid.TriangleAccumulatorsA);
        context.For(derived.WorkingWidth, derived.WorkingHeight, new TriangleMapShader(
            GetAssignment(grid, in derived), grid.Color, grid.SitePositions, grid.TriangleVertices, grid.IncidenceCounts, grid.Incidence, grid.Counts,
            derived.WorkingWidth, derived.WorkingHeight, LowPolyAbstractionSettings.AlphaThreshold));
        context.Barrier(grid.Counts);
        context.For(derived.WorkingWidth, derived.WorkingHeight, new TriangleColorPassShader(
            grid.Counts, grid.Color, grid.TriangleAccumulatorsA, grid.TriangleAccumulatorsB,
            derived.WorkingWidth, derived.WorkingHeight, 0, LowPolyAbstractionSettings.TrimSigmaFactor));
        context.Barrier(grid.TriangleAccumulatorsA);
        if (computeColors)
        {
            var lengthB = derived.TriangleCapacity * 10;
            context.For(lengthB, new FillIntShader(grid.TriangleAccumulatorsB, lengthB, 0));
            context.Barrier(grid.TriangleAccumulatorsB);
            context.For(derived.WorkingWidth, derived.WorkingHeight, new TriangleColorPassShader(
                grid.Counts, grid.Color, grid.TriangleAccumulatorsA, grid.TriangleAccumulatorsB,
                derived.WorkingWidth, derived.WorkingHeight, 1, LowPolyAbstractionSettings.TrimSigmaFactor));
            context.Barrier(grid.TriangleAccumulatorsB);
        }
        context.For(derived.TriangleCapacity, new FinalizeTrianglesShader(
            grid.TriangleAccumulatorsA, grid.TriangleAccumulatorsB, scratch, grid.TriangleColors, grid.TriangleErrors,
            derived.TriangleCapacity, computeColors ? 1 : 0));
        if (computeColors)
            context.Barrier(grid.TriangleColors);
        context.Barrier(grid.TriangleErrors);
    }

    private static void RecordScan(
        in ComputeContext context,
        LowPolyAbstractionGridResources grid,
        ReadWriteBuffer<int> scratch,
        int elementCount,
        int capLimit,
        int subtractSiteCount,
        int outputSlot)
    {
        var blocks = BlockCount(elementCount);
        context.For(blocks, new BlockCountShader(grid.Counts, grid.BlockSums, elementCount, blocks));
        context.Barrier(grid.BlockSums);
        context.For(1, new BlockPrefixShader(grid.BlockSums, scratch, blocks, capLimit, subtractSiteCount, outputSlot));
        context.Barrier(grid.BlockSums);
        context.Barrier(scratch);
    }

    private static void RecordRenderStage(
        in ComputeContext context,
        LowPolyAbstractionGridResources grid,
        ReadWriteTexture2D<Bgra32, Float4> output,
        in LowPolyAbstractionPipeline.PixelRect rect,
        in LowPolyAbstractionPipeline.DerivedValues derived,
        in LowPolyAbstractionPipeline.Parameters parameters)
    {
        context.For(rect.Width, rect.Height, new RenderShader(
            GetAssignment(grid, in derived), grid.SitePositions, grid.TriangleVertices, grid.IncidenceCounts, grid.Incidence,
            grid.TriangleColors, grid.SiteColors, output,
            rect.X, rect.Y, rect.Width, rect.Height,
            derived.WorkingWidth, derived.WorkingHeight, derived.Scale,
            Math.Clamp(parameters.Gradient, 0f, 1f),
            Math.Clamp(parameters.Wireframe, 0f, 1f) * LowPolyAbstractionSettings.MaximumWireframeWidth,
            Math.Clamp(parameters.Wireframe, 0f, 1f) > 0f ? 1f : 0f,
            Math.Clamp(parameters.Saturation, 0f, 1f),
            Math.Clamp(parameters.Jitter, 0f, 1f),
            parameters.Seed));
    }
}
