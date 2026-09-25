// Stryker disable all
using ComputeWeave;

namespace LowPolyAbstraction;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct FillIntShader(
    ReadWriteBuffer<int> values,
    int length,
    int value) : IComputeShader
{
    private readonly ReadWriteBuffer<int> values = values;
    private readonly int length = length;
    private readonly int value = value;

    public void Execute()
    {
        var index = ThreadIds.X;
        if (index >= length)
            return;
        values[index] = value;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct InitScratchShader(
    ReadWriteBuffer<int> scratch) : IComputeShader
{
    private readonly ReadWriteBuffer<int> scratch = scratch;

    public void Execute()
    {
        if (ThreadIds.X != 0)
            return;
        scratch[LowPolyAbstractionSettings.ScratchMaskHashSum] = 0;
        scratch[LowPolyAbstractionSettings.ScratchMaskHashMix] = 0;
        scratch[LowPolyAbstractionSettings.ScratchBoundsMinX] = 2147483647;
        scratch[LowPolyAbstractionSettings.ScratchBoundsMinY] = 2147483647;
        scratch[LowPolyAbstractionSettings.ScratchBoundsMaxX] = -2147483648;
        scratch[LowPolyAbstractionSettings.ScratchBoundsMaxY] = -2147483648;
        scratch[LowPolyAbstractionSettings.ScratchSiteCount] = 0;
        scratch[LowPolyAbstractionSettings.ScratchTriangleCount] = 0;
        scratch[LowPolyAbstractionSettings.ScratchInsertCount] = 0;
        scratch[LowPolyAbstractionSettings.ScratchAlphaCount] = 0;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct AnalyzeShader(
    ReadWriteTexture2D<Bgra32, Float4> source,
    ReadWriteBuffer<Float4> color,
    ReadWriteBuffer<float> luma,
    ReadWriteBuffer<int> scratch,
    int sourceOffsetX,
    int sourceOffsetY,
    int sourceWidth,
    int sourceHeight,
    int workingWidth,
    int workingHeight,
    float scale,
    float alphaThreshold) : IComputeShader
{
    private readonly ReadWriteTexture2D<Bgra32, Float4> source = source;
    private readonly ReadWriteBuffer<Float4> color = color;
    private readonly ReadWriteBuffer<float> luma = luma;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int sourceOffsetX = sourceOffsetX;
    private readonly int sourceOffsetY = sourceOffsetY;
    private readonly int sourceWidth = sourceWidth;
    private readonly int sourceHeight = sourceHeight;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;
    private readonly float scale = scale;
    private readonly float alphaThreshold = alphaThreshold;

    public void Execute()
    {
        var wx = ThreadIds.X;
        var wy = ThreadIds.Y;
        if (wx >= workingWidth || wy >= workingHeight)
            return;

        var x0 = (int)(wx * scale);
        var y0 = (int)(wy * scale);
        var x1 = Hlsl.Max((int)Hlsl.Ceil((wx + 1) * scale), x0 + 1);
        var y1 = Hlsl.Max((int)Hlsl.Ceil((wy + 1) * scale), y0 + 1);

        var sum = new Float4(0f, 0f, 0f, 0f);
        var samples = 0;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var sx = x - sourceOffsetX;
                var sy = y - sourceOffsetY;
                if (sx < 0 || sx >= sourceWidth || sy < 0 || sy >= sourceHeight)
                {
                    samples++;
                    continue;
                }
                sum += source[new Int2(sx, sy)];
                samples++;
            }
        }
        var mean = sum * (1f / Hlsl.Max(samples, 1));
        var index = wy * workingWidth + wx;
        color[index] = mean;
        var l = mean.X * 0.299f + mean.Y * 0.587f + mean.Z * 0.114f;
        luma[index] = l + mean.W;

        var quantized = (uint)(Hlsl.Saturate(mean.X) * 255f + 0.5f) |
            ((uint)(Hlsl.Saturate(mean.Y) * 255f + 0.5f) << 8) |
            ((uint)(Hlsl.Saturate(mean.Z) * 255f + 0.5f) << 16) |
            ((uint)(Hlsl.Saturate(mean.W) * 255f + 0.5f) << 24);
        var mixed = ((uint)index * 0x9E3779B9u) ^ (quantized * 0x85EBCA6Bu);
        mixed ^= mixed >> 16;
        mixed *= 0x85EBCA6Bu;
        mixed ^= mixed >> 13;
        Hlsl.InterlockedAdd(ref scratch[LowPolyAbstractionSettings.ScratchMaskHashSum], (int)mixed);
        Hlsl.InterlockedXor(ref scratch[LowPolyAbstractionSettings.ScratchMaskHashMix], (int)(mixed * 0xC2B2AE35u));

        if (mean.W > alphaThreshold)
        {
            Hlsl.InterlockedAdd(ref scratch[LowPolyAbstractionSettings.ScratchAlphaCount], 1);
            var minX = (int)(wx * scale);
            var minY = (int)(wy * scale);
            var maxX = (int)Hlsl.Ceil((wx + 1) * scale);
            var maxY = (int)Hlsl.Ceil((wy + 1) * scale);
            Hlsl.InterlockedMin(ref scratch[LowPolyAbstractionSettings.ScratchBoundsMinX], minX);
            Hlsl.InterlockedMin(ref scratch[LowPolyAbstractionSettings.ScratchBoundsMinY], minY);
            Hlsl.InterlockedMax(ref scratch[LowPolyAbstractionSettings.ScratchBoundsMaxX], maxX);
            Hlsl.InterlockedMax(ref scratch[LowPolyAbstractionSettings.ScratchBoundsMaxY], maxY);
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct EdgeShader(
    ReadWriteBuffer<float> luma,
    ReadWriteBuffer<float> edgeMagnitude,
    int workingWidth,
    int workingHeight) : IComputeShader
{
    private readonly ReadWriteBuffer<float> luma = luma;
    private readonly ReadWriteBuffer<float> edgeMagnitude = edgeMagnitude;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;

    private float Sample(int x, int y)
    {
        x = Hlsl.Clamp(x, 0, workingWidth - 1);
        y = Hlsl.Clamp(y, 0, workingHeight - 1);
        return luma[y * workingWidth + x];
    }

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= workingWidth || y >= workingHeight)
            return;

        var gx =
            Sample(x + 1, y - 1) + 2f * Sample(x + 1, y) + Sample(x + 1, y + 1) -
            Sample(x - 1, y - 1) - 2f * Sample(x - 1, y) - Sample(x - 1, y + 1);
        var gy =
            Sample(x - 1, y + 1) + 2f * Sample(x, y + 1) + Sample(x + 1, y + 1) -
            Sample(x - 1, y - 1) - 2f * Sample(x, y - 1) - Sample(x + 1, y - 1);
        edgeMagnitude[y * workingWidth + x] = Hlsl.Sqrt(gx * gx + gy * gy) * 0.25f;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct EdgeDistanceSeedShader(
    ReadWriteBuffer<float> edgeMagnitude,
    ReadWriteBuffer<int> jumpFlood,
    int workingWidth,
    int workingHeight,
    float edgeThreshold) : IComputeShader
{
    private readonly ReadWriteBuffer<float> edgeMagnitude = edgeMagnitude;
    private readonly ReadWriteBuffer<int> jumpFlood = jumpFlood;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;
    private readonly float edgeThreshold = edgeThreshold;

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= workingWidth || y >= workingHeight)
            return;
        var index = y * workingWidth + x;
        jumpFlood[index] = edgeMagnitude[index] >= edgeThreshold ? index : -1;
    }
}

[ThreadGroupSize(LowPolyAbstractionSettings.JumpFloodGroupWidth, 1, 1)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct JumpFloodPixelPassShader(
    ReadWriteBuffer<int> input,
    ReadWriteBuffer<int> output,
    int workingWidth,
    int workingHeight,
    int stepSize) : IComputeShader
{
    private readonly ReadWriteBuffer<int> input = input;
    private readonly ReadWriteBuffer<int> output = output;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;
    private readonly int stepSize = stepSize;

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= workingWidth || y >= workingHeight)
            return;

        var best = -1;
        var bestDistance = 3.402823e+38f;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var sx = x + dx * stepSize;
                var sy = y + dy * stepSize;
                if (sx < 0 || sx >= workingWidth || sy < 0 || sy >= workingHeight)
                    continue;
                var candidate = input[sy * workingWidth + sx];
                if (candidate < 0)
                    continue;
                var cx = candidate % workingWidth;
                var cy = candidate / workingWidth;
                var ddx = x - cx;
                var ddy = y - cy;
                var distance = (float)(ddx * ddx + ddy * ddy);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }
        }
        output[y * workingWidth + x] = best;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct WeightShader(
    ReadWriteBuffer<int> edgeAssignment,
    ReadWriteBuffer<Float4> color,
    ReadWriteBuffer<float> weight,
    int workingWidth,
    int workingHeight,
    float laneWidth,
    float edgeEmphasis,
    float alphaThreshold) : IComputeShader
{
    private readonly ReadWriteBuffer<int> edgeAssignment = edgeAssignment;
    private readonly ReadWriteBuffer<Float4> color = color;
    private readonly ReadWriteBuffer<float> weight = weight;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;
    private readonly float laneWidth = laneWidth;
    private readonly float edgeEmphasis = edgeEmphasis;
    private readonly float alphaThreshold = alphaThreshold;

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= workingWidth || y >= workingHeight)
            return;

        var index = y * workingWidth + x;
        if (color[index].W <= alphaThreshold)
        {
            weight[index] = 0f;
            return;
        }

        var nearest = edgeAssignment[index];
        var wave = LowPolyAbstractionSettings.LaneFloor;
        if (nearest >= 0)
        {
            var cx = nearest % workingWidth;
            var cy = nearest / workingWidth;
            var ddx = x - cx;
            var ddy = y - cy;
            var distance = Hlsl.Sqrt((float)(ddx * ddx + ddy * ddy));
            var phase = Hlsl.Abs(2f * ((distance / (2f * laneWidth)) - Hlsl.Floor(distance / (2f * laneWidth))) - 1f);
            var falloff = Hlsl.Exp(-distance / (8f * laneWidth));
            wave = LowPolyAbstractionSettings.LaneFloor + (1f - LowPolyAbstractionSettings.LaneFloor) * (1f - phase) * falloff;
        }
        weight[index] = 1f + edgeEmphasis * (Hlsl.Max(wave, LowPolyAbstractionSettings.LaneFloor) * 4f - 1f) * 0.5f + edgeEmphasis * wave * 2f;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct EdgeBestShader(
    ReadWriteBuffer<float> edgeMagnitude,
    ReadWriteBuffer<Float4> color,
    ReadWriteBuffer<int> cellBest,
    int workingWidth,
    int workingHeight,
    int cellCols,
    float spacing,
    float edgeThreshold,
    float alphaThreshold) : IComputeShader
{
    private readonly ReadWriteBuffer<float> edgeMagnitude = edgeMagnitude;
    private readonly ReadWriteBuffer<Float4> color = color;
    private readonly ReadWriteBuffer<int> cellBest = cellBest;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;
    private readonly int cellCols = cellCols;
    private readonly float spacing = spacing;
    private readonly float edgeThreshold = edgeThreshold;
    private readonly float alphaThreshold = alphaThreshold;

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= workingWidth || y >= workingHeight)
            return;

        var index = y * workingWidth + x;
        var magnitude = edgeMagnitude[index];
        if (magnitude < edgeThreshold || color[index].W <= alphaThreshold)
            return;

        var cellX = (int)(x / spacing);
        var cellY = (int)(y / spacing);
        var cell = cellY * cellCols + cellX;
        var localX = x - (int)(cellX * spacing);
        var localY = y - (int)(cellY * spacing);
        var localIndex = localY * 256 + localX;
        var quantized = Hlsl.Min((int)(magnitude * 4096f), 65535);
        var key = (quantized << 16) | (65535 - Hlsl.Min(localIndex, 65535));
        Hlsl.InterlockedMax(ref cellBest[cell], key);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct CellCountShader(
    ReadWriteBuffer<int> cellBest,
    ReadWriteBuffer<int> counts,
    int cellCount) : IComputeShader
{
    private readonly ReadWriteBuffer<int> cellBest = cellBest;
    private readonly ReadWriteBuffer<int> counts = counts;
    private readonly int cellCount = cellCount;

    public void Execute()
    {
        var index = ThreadIds.X;
        if (index >= cellCount)
            return;
        counts[index] = cellBest[index] >= 0 ? 1 : 0;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct BlockCountShader(
    ReadWriteBuffer<int> counts,
    ReadWriteBuffer<int> blockSums,
    int elementCount,
    int blockCount) : IComputeShader
{
    private readonly ReadWriteBuffer<int> counts = counts;
    private readonly ReadWriteBuffer<int> blockSums = blockSums;
    private readonly int elementCount = elementCount;
    private readonly int blockCount = blockCount;

    public void Execute()
    {
        var block = ThreadIds.X;
        if (block >= blockCount)
            return;
        var start = block * LowPolyAbstractionSettings.ScanBlockSize;
        var end = Hlsl.Min(start + LowPolyAbstractionSettings.ScanBlockSize, elementCount);
        var sum = 0;
        for (var index = start; index < end; index++)
            sum += counts[index];
        blockSums[block] = sum;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct BlockPrefixShader(
    ReadWriteBuffer<int> blockSums,
    ReadWriteBuffer<int> scratch,
    int blockCount,
    int capLimit,
    int subtractSiteCount,
    int outputSlot) : IComputeShader
{
    private readonly ReadWriteBuffer<int> blockSums = blockSums;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int blockCount = blockCount;
    private readonly int capLimit = capLimit;
    private readonly int subtractSiteCount = subtractSiteCount;
    private readonly int outputSlot = outputSlot;

    public void Execute()
    {
        if (ThreadIds.X != 0)
            return;
        var running = 0;
        for (var block = 0; block < blockCount; block++)
        {
            var value = blockSums[block];
            blockSums[block] = running;
            running += value;
        }
        var cap = capLimit;
        if (subtractSiteCount != 0)
            cap -= scratch[LowPolyAbstractionSettings.ScratchSiteCount];
        scratch[outputSlot] = Hlsl.Clamp(running, 0, Hlsl.Max(cap, 0));
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct EdgeEmitShader(
    ReadWriteBuffer<int> cellBest,
    ReadWriteBuffer<int> blockSums,
    ReadWriteBuffer<int> scratch,
    ReadWriteBuffer<Float2> sitePositions,
    ReadWriteBuffer<int> siteKinds,
    int cellCount,
    int blockCount,
    int cellCols,
    float spacing) : IComputeShader
{
    private readonly ReadWriteBuffer<int> cellBest = cellBest;
    private readonly ReadWriteBuffer<int> blockSums = blockSums;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly ReadWriteBuffer<Float2> sitePositions = sitePositions;
    private readonly ReadWriteBuffer<int> siteKinds = siteKinds;
    private readonly int cellCount = cellCount;
    private readonly int blockCount = blockCount;
    private readonly int cellCols = cellCols;
    private readonly float spacing = spacing;

    public void Execute()
    {
        var block = ThreadIds.X;
        if (block >= blockCount)
            return;
        var start = block * LowPolyAbstractionSettings.ScanBlockSize;
        var end = Hlsl.Min(start + LowPolyAbstractionSettings.ScanBlockSize, cellCount);
        var allowed = scratch[LowPolyAbstractionSettings.ScratchPendingCount];
        var baseIndex = scratch[LowPolyAbstractionSettings.ScratchSiteCount];
        var running = blockSums[block];
        for (var cell = start; cell < end; cell++)
        {
            var key = cellBest[cell];
            if (key < 0)
                continue;
            if (running < allowed)
            {
                var localIndex = 65535 - (key & 65535);
                var localX = localIndex % 256;
                var localY = localIndex / 256;
                var cellX = cell % cellCols;
                var cellY = cell / cellCols;
                var x = (int)(cellX * spacing) + localX;
                var y = (int)(cellY * spacing) + localY;
                var site = baseIndex + running;
                sitePositions[site] = new Float2(x + 0.5f, y + 0.5f);
                siteKinds[site] = 1;
            }
            running++;
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct InteriorCountShader(
    ReadWriteBuffer<Float4> color,
    ReadWriteBuffer<int> counts,
    int workingWidth,
    int workingHeight,
    int cellCols,
    int cellCount,
    float spacing,
    int seed,
    float alphaThreshold) : IComputeShader
{
    private readonly ReadWriteBuffer<Float4> color = color;
    private readonly ReadWriteBuffer<int> counts = counts;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;
    private readonly int cellCols = cellCols;
    private readonly int cellCount = cellCount;
    private readonly float spacing = spacing;
    private readonly int seed = seed;
    private readonly float alphaThreshold = alphaThreshold;

    public void Execute()
    {
        var cell = ThreadIds.X;
        if (cell >= cellCount)
            return;
        var position = LowPolyAbstractionShaderMath.JitteredCellPosition(cell, cellCols, spacing, seed);
        var x = (int)position.X;
        var y = (int)position.Y;
        var accepted = 0;
        if (x >= 0 && x < workingWidth && y >= 0 && y < workingHeight && color[y * workingWidth + x].W > alphaThreshold)
            accepted = 1;
        counts[cell] = accepted;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct InteriorEmitShader(
    ReadWriteBuffer<int> counts,
    ReadWriteBuffer<int> blockSums,
    ReadWriteBuffer<int> scratch,
    ReadWriteBuffer<Float2> sitePositions,
    ReadWriteBuffer<int> siteKinds,
    int cellCount,
    int blockCount,
    int cellCols,
    float spacing,
    int seed) : IComputeShader
{
    private readonly ReadWriteBuffer<int> counts = counts;
    private readonly ReadWriteBuffer<int> blockSums = blockSums;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly ReadWriteBuffer<Float2> sitePositions = sitePositions;
    private readonly ReadWriteBuffer<int> siteKinds = siteKinds;
    private readonly int cellCount = cellCount;
    private readonly int blockCount = blockCount;
    private readonly int cellCols = cellCols;
    private readonly float spacing = spacing;
    private readonly int seed = seed;

    public void Execute()
    {
        var block = ThreadIds.X;
        if (block >= blockCount)
            return;
        var start = block * LowPolyAbstractionSettings.ScanBlockSize;
        var end = Hlsl.Min(start + LowPolyAbstractionSettings.ScanBlockSize, cellCount);
        var allowed = scratch[LowPolyAbstractionSettings.ScratchPendingCount];
        var baseIndex = scratch[LowPolyAbstractionSettings.ScratchSiteCount];
        var running = blockSums[block];
        for (var cell = start; cell < end; cell++)
        {
            if (counts[cell] == 0)
                continue;
            if (running < allowed)
            {
                var site = baseIndex + running;
                sitePositions[site] = LowPolyAbstractionShaderMath.JitteredCellPosition(cell, cellCols, spacing, seed);
                siteKinds[site] = 0;
            }
            running++;
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct CommitSitesShader(
    ReadWriteBuffer<int> scratch) : IComputeShader
{
    private readonly ReadWriteBuffer<int> scratch = scratch;

    public void Execute()
    {
        if (ThreadIds.X != 0)
            return;
        scratch[LowPolyAbstractionSettings.ScratchSiteCount] += scratch[LowPolyAbstractionSettings.ScratchPendingCount];
        scratch[LowPolyAbstractionSettings.ScratchPendingCount] = 0;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct VoronoiClearShader(
    ReadWriteBuffer<int> jumpFlood,
    int workingWidth,
    int workingHeight) : IComputeShader
{
    private readonly ReadWriteBuffer<int> jumpFlood = jumpFlood;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= workingWidth || y >= workingHeight)
            return;
        jumpFlood[y * workingWidth + x] = -1;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct VoronoiScatterShader(
    ReadWriteBuffer<Float2> sitePositions,
    ReadWriteBuffer<int> scratch,
    ReadWriteBuffer<int> jumpFlood,
    int workingWidth,
    int workingHeight,
    int siteCapacity) : IComputeShader
{
    private readonly ReadWriteBuffer<Float2> sitePositions = sitePositions;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly ReadWriteBuffer<int> jumpFlood = jumpFlood;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;
    private readonly int siteCapacity = siteCapacity;

    public void Execute()
    {
        var site = ThreadIds.X;
        if (site >= siteCapacity || site >= scratch[LowPolyAbstractionSettings.ScratchSiteCount])
            return;
        var position = sitePositions[site];
        var x = Hlsl.Clamp((int)position.X, 0, workingWidth - 1);
        var y = Hlsl.Clamp((int)position.Y, 0, workingHeight - 1);
        Hlsl.InterlockedMax(ref jumpFlood[y * workingWidth + x], site);
    }
}

[ThreadGroupSize(LowPolyAbstractionSettings.JumpFloodGroupWidth, 1, 1)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct JumpFloodSitePassShader(
    ReadWriteBuffer<int> input,
    ReadWriteBuffer<int> output,
    ReadWriteBuffer<Float2> sitePositions,
    int workingWidth,
    int workingHeight,
    int stepSize) : IComputeShader
{
    private readonly ReadWriteBuffer<int> input = input;
    private readonly ReadWriteBuffer<int> output = output;
    private readonly ReadWriteBuffer<Float2> sitePositions = sitePositions;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;
    private readonly int stepSize = stepSize;

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= workingWidth || y >= workingHeight)
            return;

        var position = new Float2(x + 0.5f, y + 0.5f);
        var best = -1;
        var bestDistance = 3.402823e+38f;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var sx = x + dx * stepSize;
                var sy = y + dy * stepSize;
                if (sx < 0 || sx >= workingWidth || sy < 0 || sy >= workingHeight)
                    continue;
                var candidate = input[sy * workingWidth + sx];
                if (candidate < 0 || candidate == best)
                    continue;
                var delta = position - sitePositions[candidate];
                var distance = delta.X * delta.X + delta.Y * delta.Y;
                if (distance < bestDistance || (distance == bestDistance && candidate < best))
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }
        }
        output[y * workingWidth + x] = best;
    }
}

[ThreadGroupSize(LowPolyAbstractionSettings.AccumulateGroupDim, LowPolyAbstractionSettings.AccumulateGroupDim, 1)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct CentroidAccumulateShader(
    ReadWriteBuffer<int> assignment,
    ReadWriteBuffer<float> weight,
    ReadWriteBuffer<int> siteAccumulators,
    int workingWidth,
    int workingHeight) : IComputeShader
{
    private readonly ReadWriteBuffer<int> assignment = assignment;
    private readonly ReadWriteBuffer<float> weight = weight;
    private readonly ReadWriteBuffer<int> siteAccumulators = siteAccumulators;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;

    [GroupShared(LowPolyAbstractionSettings.AccumulateGroupSize)]
    private static readonly int[] groupSite = null!;

    [GroupShared(LowPolyAbstractionSettings.AccumulateGroupSize)]
    private static readonly int[] groupWeight = null!;

    [GroupShared(LowPolyAbstractionSettings.AccumulateGroupSize)]
    private static readonly int[] groupWeightedX = null!;

    [GroupShared(LowPolyAbstractionSettings.AccumulateGroupSize)]
    private static readonly int[] groupWeightedY = null!;

    private void AddCarry(int index, int value)
    {
        int previous;
        Hlsl.InterlockedAdd(ref siteAccumulators[index], value, out previous);
        if (Hlsl.AsUInt(previous) + Hlsl.AsUInt(value) < Hlsl.AsUInt(previous))
            Hlsl.InterlockedAdd(ref siteAccumulators[index + 1], 1);
    }

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        var slot = GroupIds.Index;
        var site = -1;
        var wq = 0;
        var weightedX = 0;
        var weightedY = 0;
        if (x < workingWidth && y < workingHeight)
        {
            var index = y * workingWidth + x;
            var assigned = assignment[index];
            if (assigned >= 0)
            {
                wq = Hlsl.Max((int)(weight[index] * LowPolyAbstractionSettings.WeightScale + 0.5f), 0);
                if (wq != 0)
                {
                    site = assigned;
                    weightedX = wq * (x * LowPolyAbstractionSettings.PositionSubScale + LowPolyAbstractionSettings.PositionSubScale / 2);
                    weightedY = wq * (y * LowPolyAbstractionSettings.PositionSubScale + LowPolyAbstractionSettings.PositionSubScale / 2);
                }
            }
        }
        groupSite[slot] = site;
        groupWeight[slot] = wq;
        groupWeightedX[slot] = weightedX;
        groupWeightedY[slot] = weightedY;
        Hlsl.GroupMemoryBarrierWithGroupSync();

        if (site < 0)
            return;
        for (var other = 0; other < slot; other++)
        {
            if (groupSite[other] == site)
                return;
        }
        for (var other = slot + 1; other < LowPolyAbstractionSettings.AccumulateGroupSize; other++)
        {
            if (groupSite[other] != site)
                continue;
            wq += groupWeight[other];
            weightedX += groupWeightedX[other];
            weightedY += groupWeightedY[other];
        }
        var baseIndex = site * 6;
        AddCarry(baseIndex, wq);
        AddCarry(baseIndex + 2, weightedX);
        AddCarry(baseIndex + 4, weightedY);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct UpdateSitesShader(
    ReadWriteBuffer<Float2> sitePositions,
    ReadWriteBuffer<int> siteKinds,
    ReadWriteBuffer<int> siteAccumulators,
    ReadWriteBuffer<int> scratch,
    int workingWidth,
    int workingHeight,
    int siteCapacity) : IComputeShader
{
    private readonly ReadWriteBuffer<Float2> sitePositions = sitePositions;
    private readonly ReadWriteBuffer<int> siteKinds = siteKinds;
    private readonly ReadWriteBuffer<int> siteAccumulators = siteAccumulators;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;
    private readonly int siteCapacity = siteCapacity;

    private float Reconstruct(int index)
        => siteAccumulators[index + 1] * 4294967296f + Hlsl.AsUInt(siteAccumulators[index]);

    public void Execute()
    {
        var site = ThreadIds.X;
        if (site >= siteCapacity || site >= scratch[LowPolyAbstractionSettings.ScratchSiteCount])
            return;
        if (siteKinds[site] != 0)
            return;
        var baseIndex = site * 6;
        var wsum = Reconstruct(baseIndex);
        if (wsum <= 0f)
            return;
        var cx = Reconstruct(baseIndex + 2) / (wsum * LowPolyAbstractionSettings.PositionSubScale);
        var cy = Reconstruct(baseIndex + 4) / (wsum * LowPolyAbstractionSettings.PositionSubScale);
        sitePositions[site] = new Float2(
            Hlsl.Clamp(cx, 0.5f, workingWidth - 0.5f),
            Hlsl.Clamp(cy, 0.5f, workingHeight - 0.5f));
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct CornerCountShader(
    ReadWriteBuffer<int> assignment,
    ReadWriteBuffer<int> counts,
    int workingWidth,
    int workingHeight) : IComputeShader
{
    private readonly ReadWriteBuffer<int> assignment = assignment;
    private readonly ReadWriteBuffer<int> counts = counts;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= workingWidth || y >= workingHeight)
            return;
        var index = y * workingWidth + x;
        if (x >= workingWidth - 1 || y >= workingHeight - 1)
        {
            counts[index] = 0;
            return;
        }
        var a = assignment[index];
        var b = assignment[index + 1];
        var c = assignment[index + workingWidth];
        var d = assignment[index + workingWidth + 1];
        counts[index] = LowPolyAbstractionShaderMath.CornerTriangleCount(a, b, c, d);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct TriangleEmitShader(
    ReadWriteBuffer<int> assignment,
    ReadWriteBuffer<int> counts,
    ReadWriteBuffer<int> blockSums,
    ReadWriteBuffer<int> scratch,
    ReadWriteBuffer<int> triangleVertices,
    int workingWidth,
    int workingHeight,
    int blockCount) : IComputeShader
{
    private readonly ReadWriteBuffer<int> assignment = assignment;
    private readonly ReadWriteBuffer<int> counts = counts;
    private readonly ReadWriteBuffer<int> blockSums = blockSums;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly ReadWriteBuffer<int> triangleVertices = triangleVertices;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;
    private readonly int blockCount = blockCount;

    public void Execute()
    {
        var block = ThreadIds.X;
        if (block >= blockCount)
            return;
        var elementCount = workingWidth * workingHeight;
        var start = block * LowPolyAbstractionSettings.ScanBlockSize;
        var end = Hlsl.Min(start + LowPolyAbstractionSettings.ScanBlockSize, elementCount);
        var allowed = scratch[LowPolyAbstractionSettings.ScratchTriangleCount];
        var running = blockSums[block];
        for (var index = start; index < end; index++)
        {
            var count = counts[index];
            if (count == 0)
                continue;
            var a = assignment[index];
            var b = assignment[index + 1];
            var c = assignment[index + workingWidth];
            var d = assignment[index + workingWidth + 1];
            if (count == 1)
            {
                if (running < allowed)
                {
                    var v1 = -1;
                    var v2 = -1;
                    if (b != a)
                        v1 = b;
                    if (c != a && c != v1)
                    {
                        if (v1 < 0)
                            v1 = c;
                        else
                            v2 = c;
                    }
                    if (d != a && d != v1 && d != v2 && v2 < 0)
                    {
                        if (v1 < 0)
                            v1 = d;
                        else
                            v2 = d;
                    }
                    triangleVertices[running * 3] = a;
                    triangleVertices[running * 3 + 1] = v1;
                    triangleVertices[running * 3 + 2] = v2;
                }
                running++;
            }
            else
            {
                if (running < allowed)
                {
                    triangleVertices[running * 3] = a;
                    triangleVertices[running * 3 + 1] = b;
                    triangleVertices[running * 3 + 2] = d;
                }
                running++;
                if (running < allowed)
                {
                    triangleVertices[running * 3] = a;
                    triangleVertices[running * 3 + 1] = d;
                    triangleVertices[running * 3 + 2] = c;
                }
                running++;
            }
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct IncidenceBuildShader(
    ReadWriteBuffer<int> triangleVertices,
    ReadWriteBuffer<int> incidenceCounts,
    ReadWriteBuffer<int> incidence,
    ReadWriteBuffer<int> scratch,
    int triangleCapacity) : IComputeShader
{
    private readonly ReadWriteBuffer<int> triangleVertices = triangleVertices;
    private readonly ReadWriteBuffer<int> incidenceCounts = incidenceCounts;
    private readonly ReadWriteBuffer<int> incidence = incidence;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int triangleCapacity = triangleCapacity;

    public void Execute()
    {
        var triangle = ThreadIds.X;
        if (triangle >= triangleCapacity || triangle >= scratch[LowPolyAbstractionSettings.ScratchTriangleCount])
            return;
        for (var vertex = 0; vertex < 3; vertex++)
        {
            var site = triangleVertices[triangle * 3 + vertex];
            int slot;
            Hlsl.InterlockedAdd(ref incidenceCounts[site], 1, out slot);
            if (slot < LowPolyAbstractionSettings.MaxIncidence)
                incidence[site * LowPolyAbstractionSettings.MaxIncidence + slot] = triangle;
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct SortIncidenceShader(
    ReadWriteBuffer<int> incidenceCounts,
    ReadWriteBuffer<int> incidence,
    int siteCapacity) : IComputeShader
{
    private readonly ReadWriteBuffer<int> incidenceCounts = incidenceCounts;
    private readonly ReadWriteBuffer<int> incidence = incidence;
    private readonly int siteCapacity = siteCapacity;

    public void Execute()
    {
        var site = ThreadIds.X;
        if (site >= siteCapacity)
            return;
        var listed = Hlsl.Min(incidenceCounts[site], LowPolyAbstractionSettings.MaxIncidence);
        var baseIndex = site * LowPolyAbstractionSettings.MaxIncidence;
        for (var i = 1; i < listed; i++)
        {
            var value = incidence[baseIndex + i];
            var j = i - 1;
            while (j >= 0 && incidence[baseIndex + j] > value)
            {
                incidence[baseIndex + j + 1] = incidence[baseIndex + j];
                j--;
            }
            incidence[baseIndex + j + 1] = value;
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct TriangleMapShader(
    ReadWriteBuffer<int> assignment,
    ReadWriteBuffer<Float4> color,
    ReadWriteBuffer<Float2> sitePositions,
    ReadWriteBuffer<int> triangleVertices,
    ReadWriteBuffer<int> incidenceCounts,
    ReadWriteBuffer<int> incidence,
    ReadWriteBuffer<int> triangleMap,
    int workingWidth,
    int workingHeight,
    float alphaThreshold) : IComputeShader
{
    private readonly ReadWriteBuffer<int> assignment = assignment;
    private readonly ReadWriteBuffer<Float4> color = color;
    private readonly ReadWriteBuffer<Float2> sitePositions = sitePositions;
    private readonly ReadWriteBuffer<int> triangleVertices = triangleVertices;
    private readonly ReadWriteBuffer<int> incidenceCounts = incidenceCounts;
    private readonly ReadWriteBuffer<int> incidence = incidence;
    private readonly ReadWriteBuffer<int> triangleMap = triangleMap;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;
    private readonly float alphaThreshold = alphaThreshold;

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= workingWidth || y >= workingHeight)
            return;

        var index = y * workingWidth + x;
        if (color[index].W <= alphaThreshold)
        {
            triangleMap[index] = -1;
            return;
        }
        var site = assignment[index];
        if (site < 0)
        {
            triangleMap[index] = -1;
            return;
        }
        var px = x + 0.5f;
        var py = y + 0.5f;
        var listed = Hlsl.Min(incidenceCounts[site], LowPolyAbstractionSettings.MaxIncidence);
        var best = -1;
        for (var slot = 0; slot < listed; slot++)
        {
            var triangle = incidence[site * LowPolyAbstractionSettings.MaxIncidence + slot];
            if (best >= 0 && triangle >= best)
                continue;
            var a = sitePositions[triangleVertices[triangle * 3]];
            var b = sitePositions[triangleVertices[triangle * 3 + 1]];
            var c = sitePositions[triangleVertices[triangle * 3 + 2]];
            if (LowPolyAbstractionShaderMath.ContainsPoint(a, b, c, px, py))
                best = triangle;
        }
        triangleMap[index] = best;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct TriangleColorPassShader(
    ReadWriteBuffer<int> triangleMap,
    ReadWriteBuffer<Float4> color,
    ReadWriteBuffer<int> accumulatorsA,
    ReadWriteBuffer<int> accumulatorsB,
    int workingWidth,
    int workingHeight,
    int trimmedPass,
    float trimSigmaFactor) : IComputeShader
{
    private readonly ReadWriteBuffer<int> triangleMap = triangleMap;
    private readonly ReadWriteBuffer<Float4> color = color;
    private readonly ReadWriteBuffer<int> accumulatorsA = accumulatorsA;
    private readonly ReadWriteBuffer<int> accumulatorsB = accumulatorsB;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;
    private readonly int trimmedPass = trimmedPass;
    private readonly float trimSigmaFactor = trimSigmaFactor;

    private float ReconstructA(int index)
        => accumulatorsA[index + 1] * 4294967296f + Hlsl.AsUInt(accumulatorsA[index]);

    private void AddCarryA(int index, int value)
    {
        int previous;
        Hlsl.InterlockedAdd(ref accumulatorsA[index], value, out previous);
        if (Hlsl.AsUInt(previous) + Hlsl.AsUInt(value) < Hlsl.AsUInt(previous))
            Hlsl.InterlockedAdd(ref accumulatorsA[index + 1], 1);
    }

    private void AddCarryB(int index, int value)
    {
        int previous;
        Hlsl.InterlockedAdd(ref accumulatorsB[index], value, out previous);
        if (Hlsl.AsUInt(previous) + Hlsl.AsUInt(value) < Hlsl.AsUInt(previous))
            Hlsl.InterlockedAdd(ref accumulatorsB[index + 1], 1);
    }

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= workingWidth || y >= workingHeight)
            return;

        var index = y * workingWidth + x;
        var triangle = triangleMap[index];
        if (triangle < 0)
            return;
        var pixel = color[index];
        var l = pixel.X * 0.299f + pixel.Y * 0.587f + pixel.Z * 0.114f;
        var lq = (int)(Hlsl.Saturate(l) * LowPolyAbstractionSettings.ColorScale + 0.5f);
        if (trimmedPass != 0)
        {
            var baseA = triangle * 14;
            var count = ReconstructA(baseA);
            if (count > 0f)
            {
                var meanL = ReconstructA(baseA + 10) / (count * LowPolyAbstractionSettings.ColorScale);
                var meanL2 = ReconstructA(baseA + 12) / (count * (float)LowPolyAbstractionSettings.ColorScale);
                var variance = Hlsl.Max(meanL2 - meanL * meanL, 0f);
                var band = trimSigmaFactor * Hlsl.Sqrt(variance) + 1f / LowPolyAbstractionSettings.ColorScale;
                if (Hlsl.Abs(l - meanL) > band)
                    return;
            }
            var baseB = triangle * 10;
            AddCarryB(baseB, 1);
            AddCarryB(baseB + 2, (int)(Hlsl.Saturate(pixel.X) * LowPolyAbstractionSettings.ColorScale + 0.5f));
            AddCarryB(baseB + 4, (int)(Hlsl.Saturate(pixel.Y) * LowPolyAbstractionSettings.ColorScale + 0.5f));
            AddCarryB(baseB + 6, (int)(Hlsl.Saturate(pixel.Z) * LowPolyAbstractionSettings.ColorScale + 0.5f));
            AddCarryB(baseB + 8, (int)(Hlsl.Saturate(pixel.W) * LowPolyAbstractionSettings.ColorScale + 0.5f));
        }
        else
        {
            var baseA = triangle * 14;
            AddCarryA(baseA, 1);
            AddCarryA(baseA + 2, (int)(Hlsl.Saturate(pixel.X) * LowPolyAbstractionSettings.ColorScale + 0.5f));
            AddCarryA(baseA + 4, (int)(Hlsl.Saturate(pixel.Y) * LowPolyAbstractionSettings.ColorScale + 0.5f));
            AddCarryA(baseA + 6, (int)(Hlsl.Saturate(pixel.Z) * LowPolyAbstractionSettings.ColorScale + 0.5f));
            AddCarryA(baseA + 8, (int)(Hlsl.Saturate(pixel.W) * LowPolyAbstractionSettings.ColorScale + 0.5f));
            AddCarryA(baseA + 10, lq);
            AddCarryA(baseA + 12, (int)((float)lq * lq / LowPolyAbstractionSettings.ColorScale + 0.5f));
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct FinalizeTrianglesShader(
    ReadWriteBuffer<int> accumulatorsA,
    ReadWriteBuffer<int> accumulatorsB,
    ReadWriteBuffer<int> scratch,
    ReadWriteBuffer<Float4> triangleColors,
    ReadWriteBuffer<float> triangleErrors,
    int triangleCapacity,
    int computeColors) : IComputeShader
{
    private readonly ReadWriteBuffer<int> accumulatorsA = accumulatorsA;
    private readonly ReadWriteBuffer<int> accumulatorsB = accumulatorsB;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly ReadWriteBuffer<Float4> triangleColors = triangleColors;
    private readonly ReadWriteBuffer<float> triangleErrors = triangleErrors;
    private readonly int triangleCapacity = triangleCapacity;
    private readonly int computeColors = computeColors;

    private float ReconstructA(int index)
        => accumulatorsA[index + 1] * 4294967296f + Hlsl.AsUInt(accumulatorsA[index]);

    private float ReconstructB(int index)
        => accumulatorsB[index + 1] * 4294967296f + Hlsl.AsUInt(accumulatorsB[index]);

    public void Execute()
    {
        var triangle = ThreadIds.X;
        if (triangle >= triangleCapacity)
            return;
        if (triangle >= scratch[LowPolyAbstractionSettings.ScratchTriangleCount])
        {
            if (computeColors != 0)
                triangleColors[triangle] = new Float4(0f, 0f, 0f, 0f);
            triangleErrors[triangle] = 0f;
            return;
        }

        var baseA = triangle * 14;
        var countA = ReconstructA(baseA);
        if (countA <= 0f)
        {
            if (computeColors != 0)
                triangleColors[triangle] = new Float4(0f, 0f, 0f, 0f);
            triangleErrors[triangle] = 0f;
            return;
        }
        var inverseScaleA = 1f / (countA * LowPolyAbstractionSettings.ColorScale);
        var meanL = ReconstructA(baseA + 10) * inverseScaleA;
        var meanL2 = ReconstructA(baseA + 12) / (countA * (float)LowPolyAbstractionSettings.ColorScale);
        triangleErrors[triangle] = Hlsl.Max(meanL2 - meanL * meanL, 0f);
        if (computeColors == 0)
            return;

        var baseB = triangle * 10;
        var countB = ReconstructB(baseB);
        if (countB > 0f)
        {
            var inverseScaleB = 1f / (countB * LowPolyAbstractionSettings.ColorScale);
            triangleColors[triangle] = new Float4(
                ReconstructB(baseB + 2) * inverseScaleB,
                ReconstructB(baseB + 4) * inverseScaleB,
                ReconstructB(baseB + 6) * inverseScaleB,
                ReconstructB(baseB + 8) * inverseScaleB);
        }
        else
        {
            triangleColors[triangle] = new Float4(
                ReconstructA(baseA + 2) * inverseScaleA,
                ReconstructA(baseA + 4) * inverseScaleA,
                ReconstructA(baseA + 6) * inverseScaleA,
                ReconstructA(baseA + 8) * inverseScaleA);
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct RefineCountShader(
    ReadWriteBuffer<float> triangleErrors,
    ReadWriteBuffer<int> scratch,
    ReadWriteBuffer<int> counts,
    int triangleCapacity,
    float errorThreshold) : IComputeShader
{
    private readonly ReadWriteBuffer<float> triangleErrors = triangleErrors;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly ReadWriteBuffer<int> counts = counts;
    private readonly int triangleCapacity = triangleCapacity;
    private readonly float errorThreshold = errorThreshold;

    public void Execute()
    {
        var triangle = ThreadIds.X;
        if (triangle >= triangleCapacity)
            return;
        var accepted = 0;
        if (triangle < scratch[LowPolyAbstractionSettings.ScratchTriangleCount] && triangleErrors[triangle] > errorThreshold)
            accepted = 1;
        counts[triangle] = accepted;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct RefineEmitShader(
    ReadWriteBuffer<int> counts,
    ReadWriteBuffer<int> blockSums,
    ReadWriteBuffer<int> scratch,
    ReadWriteBuffer<int> triangleVertices,
    ReadWriteBuffer<Float2> sitePositions,
    ReadWriteBuffer<int> siteKinds,
    int triangleCapacity,
    int blockCount) : IComputeShader
{
    private readonly ReadWriteBuffer<int> counts = counts;
    private readonly ReadWriteBuffer<int> blockSums = blockSums;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly ReadWriteBuffer<int> triangleVertices = triangleVertices;
    private readonly ReadWriteBuffer<Float2> sitePositions = sitePositions;
    private readonly ReadWriteBuffer<int> siteKinds = siteKinds;
    private readonly int triangleCapacity = triangleCapacity;
    private readonly int blockCount = blockCount;

    public void Execute()
    {
        var block = ThreadIds.X;
        if (block >= blockCount)
            return;
        var start = block * LowPolyAbstractionSettings.ScanBlockSize;
        var end = Hlsl.Min(start + LowPolyAbstractionSettings.ScanBlockSize, triangleCapacity);
        var allowed = scratch[LowPolyAbstractionSettings.ScratchPendingCount];
        var baseIndex = scratch[LowPolyAbstractionSettings.ScratchSiteCount];
        var running = blockSums[block];
        for (var triangle = start; triangle < end; triangle++)
        {
            if (counts[triangle] == 0)
                continue;
            if (running < allowed)
            {
                var a = sitePositions[triangleVertices[triangle * 3]];
                var b = sitePositions[triangleVertices[triangle * 3 + 1]];
                var c = sitePositions[triangleVertices[triangle * 3 + 2]];
                var site = baseIndex + running;
                sitePositions[site] = (a + b + c) * (1f / 3f);
                siteKinds[site] = 0;
            }
            running++;
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct SiteColorAccumulateShader(
    ReadWriteBuffer<int> assignment,
    ReadWriteBuffer<Float4> color,
    ReadWriteBuffer<int> siteColorAccumulators,
    int workingWidth,
    int workingHeight,
    float alphaThreshold) : IComputeShader
{
    private readonly ReadWriteBuffer<int> assignment = assignment;
    private readonly ReadWriteBuffer<Float4> color = color;
    private readonly ReadWriteBuffer<int> siteColorAccumulators = siteColorAccumulators;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;
    private readonly float alphaThreshold = alphaThreshold;

    private void AddCarry(int index, int value)
    {
        int previous;
        Hlsl.InterlockedAdd(ref siteColorAccumulators[index], value, out previous);
        if (Hlsl.AsUInt(previous) + Hlsl.AsUInt(value) < Hlsl.AsUInt(previous))
            Hlsl.InterlockedAdd(ref siteColorAccumulators[index + 1], 1);
    }

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= workingWidth || y >= workingHeight)
            return;
        var index = y * workingWidth + x;
        var pixel = color[index];
        if (pixel.W <= alphaThreshold)
            return;
        var site = assignment[index];
        if (site < 0)
            return;
        var baseIndex = site * 10;
        AddCarry(baseIndex, 1);
        AddCarry(baseIndex + 2, (int)(Hlsl.Saturate(pixel.X) * LowPolyAbstractionSettings.ColorScale + 0.5f));
        AddCarry(baseIndex + 4, (int)(Hlsl.Saturate(pixel.Y) * LowPolyAbstractionSettings.ColorScale + 0.5f));
        AddCarry(baseIndex + 6, (int)(Hlsl.Saturate(pixel.Z) * LowPolyAbstractionSettings.ColorScale + 0.5f));
        AddCarry(baseIndex + 8, (int)(Hlsl.Saturate(pixel.W) * LowPolyAbstractionSettings.ColorScale + 0.5f));
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct SiteColorFinalizeShader(
    ReadWriteBuffer<int> siteColorAccumulators,
    ReadWriteBuffer<Float4> siteColors,
    int siteCapacity) : IComputeShader
{
    private readonly ReadWriteBuffer<int> siteColorAccumulators = siteColorAccumulators;
    private readonly ReadWriteBuffer<Float4> siteColors = siteColors;
    private readonly int siteCapacity = siteCapacity;

    private float Reconstruct(int index)
        => siteColorAccumulators[index + 1] * 4294967296f + Hlsl.AsUInt(siteColorAccumulators[index]);

    public void Execute()
    {
        var site = ThreadIds.X;
        if (site >= siteCapacity)
            return;
        var baseIndex = site * 10;
        var count = Reconstruct(baseIndex);
        if (count <= 0f)
        {
            siteColors[site] = new Float4(0f, 0f, 0f, 0f);
            return;
        }
        var inverseScale = 1f / (count * LowPolyAbstractionSettings.ColorScale);
        siteColors[site] = new Float4(
            Reconstruct(baseIndex + 2) * inverseScale,
            Reconstruct(baseIndex + 4) * inverseScale,
            Reconstruct(baseIndex + 6) * inverseScale,
            Reconstruct(baseIndex + 8) * inverseScale);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct RenderShader(
    ReadWriteBuffer<int> assignment,
    ReadWriteBuffer<Float2> sitePositions,
    ReadWriteBuffer<int> triangleVertices,
    ReadWriteBuffer<int> incidenceCounts,
    ReadWriteBuffer<int> incidence,
    ReadWriteBuffer<Float4> triangleColors,
    ReadWriteBuffer<Float4> siteColors,
    ReadWriteBuffer<Float4> color,
    ReadWriteTexture2D<Bgra32, Float4> output,
    int rectOffsetX,
    int rectOffsetY,
    int rectWidth,
    int rectHeight,
    int workingWidth,
    int workingHeight,
    float scale,
    float gradientAmount,
    float wireframeWidth,
    float wireframeStrength,
    float saturation,
    float jitterAmplitude,
    int seed) : IComputeShader
{
    private readonly ReadWriteBuffer<int> assignment = assignment;
    private readonly ReadWriteBuffer<Float2> sitePositions = sitePositions;
    private readonly ReadWriteBuffer<int> triangleVertices = triangleVertices;
    private readonly ReadWriteBuffer<int> incidenceCounts = incidenceCounts;
    private readonly ReadWriteBuffer<int> incidence = incidence;
    private readonly ReadWriteBuffer<Float4> triangleColors = triangleColors;
    private readonly ReadWriteBuffer<Float4> siteColors = siteColors;
    private readonly ReadWriteBuffer<Float4> color = color;
    private readonly ReadWriteTexture2D<Bgra32, Float4> output = output;
    private readonly int rectOffsetX = rectOffsetX;
    private readonly int rectOffsetY = rectOffsetY;
    private readonly int rectWidth = rectWidth;
    private readonly int rectHeight = rectHeight;
    private readonly int workingWidth = workingWidth;
    private readonly int workingHeight = workingHeight;
    private readonly float scale = scale;
    private readonly float gradientAmount = gradientAmount;
    private readonly float wireframeWidth = wireframeWidth;
    private readonly float wireframeStrength = wireframeStrength;
    private readonly float saturation = saturation;
    private readonly float jitterAmplitude = jitterAmplitude;
    private readonly int seed = seed;

    private int FindTriangle(float px, float py, int site)
    {
        var listed = Hlsl.Min(incidenceCounts[site], LowPolyAbstractionSettings.MaxIncidence);
        var best = -1;
        for (var slot = 0; slot < listed; slot++)
        {
            var triangle = incidence[site * LowPolyAbstractionSettings.MaxIncidence + slot];
            if (best >= 0 && triangle >= best)
                continue;
            var a = sitePositions[triangleVertices[triangle * 3]];
            var b = sitePositions[triangleVertices[triangle * 3 + 1]];
            var c = sitePositions[triangleVertices[triangle * 3 + 2]];
            if (LowPolyAbstractionShaderMath.ContainsPoint(a, b, c, px, py))
                best = triangle;
        }
        return best;
    }

    private float SourceAlpha(float x, float y)
    {
        var fx = Hlsl.Clamp(x - 0.5f, 0f, workingWidth - 1f);
        var fy = Hlsl.Clamp(y - 0.5f, 0f, workingHeight - 1f);
        var x0 = (int)fx;
        var y0 = (int)fy;
        var x1 = Hlsl.Min(x0 + 1, workingWidth - 1);
        var y1 = Hlsl.Min(y0 + 1, workingHeight - 1);
        var tx = fx - x0;
        var ty = fy - y0;
        var top = Hlsl.Lerp(color[y0 * workingWidth + x0].W, color[y0 * workingWidth + x1].W, tx);
        var bottom = Hlsl.Lerp(color[y1 * workingWidth + x0].W, color[y1 * workingWidth + x1].W, tx);
        return Hlsl.Saturate(Hlsl.Lerp(top, bottom, ty));
    }

    public void Execute()
    {
        if (ThreadIds.X >= rectWidth || ThreadIds.Y >= rectHeight)
            return;
        var px = ThreadIds.X + rectOffsetX + 0.5f;
        var py = ThreadIds.Y + rectOffsetY + 0.5f;
        var wxf = px / scale;
        var wyf = py / scale;
        var wx = Hlsl.Clamp((int)wxf, 0, workingWidth - 1);
        var wy = Hlsl.Clamp((int)wyf, 0, workingHeight - 1);
        var site = assignment[wy * workingWidth + wx];
        if (site < 0)
        {
            output[ThreadIds.XY] = new Float4(0f, 0f, 0f, 0f);
            return;
        }

        var triangle = FindTriangle(wxf, wyf, site);
        Float4 premultiplied;
        var edgeDistance = 1e+6f;
        if (triangle < 0)
        {
            output[ThreadIds.XY] = new Float4(0f, 0f, 0f, 0f);
            return;
        }

        var i0 = triangleVertices[triangle * 3];
        var i1 = triangleVertices[triangle * 3 + 1];
        var i2 = triangleVertices[triangle * 3 + 2];
        var a = sitePositions[i0];
        var b = sitePositions[i1];
        var c = sitePositions[i2];
        var bary = LowPolyAbstractionShaderMath.Barycentric(a, b, c, wxf, wyf);
        var flat = triangleColors[triangle];
        var gouraud = siteColors[i0] * bary.X + siteColors[i1] * bary.Y + siteColors[i2] * bary.Z;
        premultiplied = Hlsl.Lerp(flat, gouraud, gradientAmount);

        edgeDistance = Hlsl.Min(
            LowPolyAbstractionShaderMath.EdgeDistance(a, b, wxf, wyf),
            Hlsl.Min(
                LowPolyAbstractionShaderMath.EdgeDistance(b, c, wxf, wyf),
                LowPolyAbstractionShaderMath.EdgeDistance(c, a, wxf, wyf))) * scale;

        var alpha = Hlsl.Saturate(premultiplied.W);
        if (alpha <= 0f)
        {
            output[ThreadIds.XY] = new Float4(0f, 0f, 0f, 0f);
            return;
        }

        var straight = new Float3(premultiplied.X, premultiplied.Y, premultiplied.Z) * (1f / Hlsl.Max(alpha, 1e-4f));
        var mean = straight.X * 0.299f + straight.Y * 0.587f + straight.Z * 0.114f;
        straight = new Float3(mean, mean, mean) + (straight - new Float3(mean, mean, mean)) * (1f + saturation * LowPolyAbstractionSettings.MaximumSaturationBoost);
        if (jitterAmplitude > 0f)
        {
            var centroid = (a + b + c) * (1f / 3f);
            var hash = LowPolyAbstractionShaderMath.Hash01(
                (uint)(centroid.X * 64f) * 0x9E3779B9u ^ (uint)(centroid.Y * 64f) * 0x85EBCA6Bu ^ (uint)seed * 0xC2B2AE35u);
            straight *= 1f + jitterAmplitude * LowPolyAbstractionSettings.MaximumLuminanceJitter * (hash * 2f - 1f);
        }
        if (wireframeStrength > 0f && wireframeWidth > 0f)
        {
            var line = 1f - Hlsl.Saturate(edgeDistance / wireframeWidth);
            straight *= 1f - wireframeStrength * 0.65f * line;
        }
        straight = Hlsl.Saturate(straight);
        alpha = Hlsl.Min(alpha, SourceAlpha(wxf, wyf));

        var r = Hlsl.Min(straight.X * alpha, alpha);
        var g = Hlsl.Min(straight.Y * alpha, alpha);
        var bl = Hlsl.Min(straight.Z * alpha, alpha);
        output[ThreadIds.XY] = new Float4(r, g, bl, alpha);
    }
}

internal static class LowPolyAbstractionShaderMath
{
    public static float Hash01(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return value * 2.3283064e-10f;
    }

    public static Float2 JitteredCellPosition(int cell, int cellCols, float spacing, int seed)
    {
        var cellX = cell % cellCols;
        var cellY = cell / cellCols;
        var h = (uint)cell * 0x9E3779B9u ^ (uint)seed * 0xC2B2AE35u;
        var jx = Hash01(h);
        var jy = Hash01(h ^ 0x85EBCA6Bu);
        return new Float2((cellX + 0.1f + 0.8f * jx) * spacing, (cellY + 0.1f + 0.8f * jy) * spacing);
    }

    public static int CornerTriangleCount(int a, int b, int c, int d)
    {
        if (a < 0 || b < 0 || c < 0 || d < 0)
            return 0;
        var distinct = 1;
        if (b != a)
            distinct++;
        if (c != a && c != b)
            distinct++;
        if (d != a && d != b && d != c)
            distinct++;
        if (distinct == 3)
            return 1;
        return distinct == 4 ? 2 : 0;
    }

    public static Float3 Barycentric(Float2 a, Float2 b, Float2 c, float px, float py)
    {
        var v0 = b - a;
        var v1 = c - a;
        var v2 = new Float2(px - a.X, py - a.Y);
        var d00 = v0.X * v0.X + v0.Y * v0.Y;
        var d01 = v0.X * v1.X + v0.Y * v1.Y;
        var d11 = v1.X * v1.X + v1.Y * v1.Y;
        var d20 = v2.X * v0.X + v2.Y * v0.Y;
        var d21 = v2.X * v1.X + v2.Y * v1.Y;
        var denominator = d00 * d11 - d01 * d01;
        if (Hlsl.Abs(denominator) < 1e-12f)
            return new Float3(1f, 0f, 0f);
        var v = (d11 * d20 - d01 * d21) / denominator;
        var w = (d00 * d21 - d01 * d20) / denominator;
        return new Float3(1f - v - w, v, w);
    }

    public static bool ContainsPoint(Float2 a, Float2 b, Float2 c, float px, float py)
    {
        var v0 = b - a;
        var v1 = c - a;
        if (Hlsl.Abs(v0.X * v1.Y - v0.Y * v1.X) < 1e-6f)
            return false;
        var bary = Barycentric(a, b, c, px, py);
        return bary.X >= -1e-4f && bary.Y >= -1e-4f && bary.Z >= -1e-4f;
    }

    public static float EdgeDistance(Float2 a, Float2 b, float px, float py)
    {
        var abx = b.X - a.X;
        var aby = b.Y - a.Y;
        var apx = px - a.X;
        var apy = py - a.Y;
        var lengthSquared = abx * abx + aby * aby;
        if (lengthSquared < 1e-12f)
            return Hlsl.Sqrt(apx * apx + apy * apy);
        var t = Hlsl.Saturate((apx * abx + apy * aby) / lengthSquared);
        var dx = apx - t * abx;
        var dy = apy - t * aby;
        return Hlsl.Sqrt(dx * dx + dy * dy);
    }
}
