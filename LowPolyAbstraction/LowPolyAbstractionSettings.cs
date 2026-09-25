namespace LowPolyAbstraction;

internal static class LowPolyAbstractionSettings
{
    public const int MaximumCanvasSize = 8192;
    public const int MarginPadding = 8;
    public const int MinimumWorkingSize = 16;
    public const int ScratchLength = 16;
    public const int ScratchMaskHashSum = 0;
    public const int ScratchMaskHashMix = 1;
    public const int ScratchBoundsMinX = 2;
    public const int ScratchBoundsMinY = 3;
    public const int ScratchBoundsMaxX = 4;
    public const int ScratchBoundsMaxY = 5;
    public const int ScratchSiteCount = 6;
    public const int ScratchTriangleCount = 7;
    public const int ScratchInsertCount = 8;
    public const int ScratchAlphaCount = 9;
    public const int ScratchPendingCount = 10;
    public const int ScanBlockSize = 1024;
    public const int JumpFloodGroupWidth = 64;
    public const int AccumulateGroupDim = 8;
    public const int AccumulateGroupSize = AccumulateGroupDim * AccumulateGroupDim;
    public const float AlphaThreshold = 0.05f;
    public const float EdgeThreshold = 0.06f;
    public const float EdgeSampleSpacingFactor = 0.02f;
    public const float MinimumSpacingScale = 0.6f;
    public const float MaximumSpacingScale = 2.0f;
    public const float MinimumSiteFraction = 0.1f;
    public const float EdgeSiteBudgetFraction = 0.4f;
    public const float LaneFloor = 0.08f;
    public const int WeightScale = 64;
    public const int PositionSubScale = 4;
    public const int ColorScale = 4096;
    public const int MaxIncidence = 24;
    public const int PostInsertLloydIterations = 2;
    public const float TrimSigmaFactor = 1.2f;
    public const float MinimumErrorThreshold = 0.0004f;
    public const float MaximumErrorThreshold = 0.02f;
    public const float MaximumSaturationBoost = 0.6f;
    public const float MaximumLuminanceJitter = 0.08f;
    public const float WireframeWidth = 1.5f;
    public const float SiteCapacityFraction = 1.5f;
    public const int TriangleCapacitySlack = 16;
    public const int MaximumPendingSubmissions = 32;

    public static QualitySettings GetQuality(LowPolyAbstractionQuality quality)
        => quality switch
        {
            LowPolyAbstractionQuality.Balanced => new QualitySettings(1024, 4096, 6, 1),
            LowPolyAbstractionQuality.Ultra => new QualitySettings(2048, 16384, 10, 3),
            _ => new QualitySettings(1536, 8192, 8, 2),
        };

    public static (int Width, int Height, float Scale) GetWorkingSize(int width, int height, int resolution)
    {
        var longSide = Math.Max(Math.Max(width, height), 1);
        var target = Math.Min(resolution, longSide);
        var scale = longSide / (float)target;
        var workingWidth = Math.Max((int)Math.Ceiling(width / scale), MinimumWorkingSize);
        var workingHeight = Math.Max((int)Math.Ceiling(height / scale), MinimumWorkingSize);
        return (workingWidth, workingHeight, scale);
    }

    public static int GetSiteCapacity(int baseSites)
        => (int)(baseSites * SiteCapacityFraction);

    public static int GetTriangleCapacity(int siteCapacity)
        => siteCapacity * 2 + TriangleCapacitySlack;

    public static int GetSiteCount(float detail, int baseSites)
        => Math.Max((int)(baseSites * (MinimumSiteFraction + (1f - MinimumSiteFraction) * Math.Clamp(detail, 0f, 1f))), 64);

    public static float GetEdgeSampleSpacing(int workingWidth, int workingHeight, float fidelity)
    {
        var unit = EdgeSampleSpacingFactor * (workingWidth + workingHeight);
        var scale = MaximumSpacingScale + Math.Clamp(fidelity, 0f, 1f) * (MinimumSpacingScale - MaximumSpacingScale);
        return Math.Max(unit * scale, 2f);
    }

    public static float GetLaneWidth(int workingWidth, int workingHeight)
        => Math.Max(EdgeSampleSpacingFactor * (workingWidth + workingHeight) * 0.5f, 1f);

    public static float GetErrorThreshold(float refine)
        => MaximumErrorThreshold + Math.Clamp(refine, 0f, 1f) * (MinimumErrorThreshold - MaximumErrorThreshold);

    public static int GetJumpFloodInitialStep(int width, int height)
    {
        var maxSide = Math.Max(Math.Max(width, height), 1);
        var step = 1;
        while (step < maxSide)
            step <<= 1;
        return Math.Max(step >> 1, 1);
    }

    internal readonly record struct QualitySettings(int WorkingResolution, int BaseSites, int CvtIterations, int RefinePasses);
}
