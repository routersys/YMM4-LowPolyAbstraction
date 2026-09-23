namespace LowPolyAbstraction.Tests;

public sealed class LowPolyAbstractionSettingsTests
{
    [Theory]
    [InlineData(LowPolyAbstractionQuality.Balanced, 1024, 4096, 6, 1)]
    [InlineData(LowPolyAbstractionQuality.High, 1536, 8192, 8, 2)]
    [InlineData(LowPolyAbstractionQuality.Ultra, 2048, 16384, 10, 3)]
    public void EveryQualityChoosesItsResolutionSitesAndIterations(LowPolyAbstractionQuality quality, int resolution, int sites, int cvtIterations, int refinePasses)
    {
        var settings = LowPolyAbstractionSettings.GetQuality(quality);

        Assert.Equal(resolution, settings.WorkingResolution);
        Assert.Equal(sites, settings.BaseSites);
        Assert.Equal(cvtIterations, settings.CvtIterations);
        Assert.Equal(refinePasses, settings.RefinePasses);
    }

    [Theory]
    [InlineData(1920, 1080, 1536)]
    [InlineData(1080, 1920, 1536)]
    [InlineData(8, 8, 1536)]
    [InlineData(4096, 16, 1024)]
    [InlineData(100, 100, 2048)]
    public void TheWorkingGridCoversTheWholeCanvas(int width, int height, int resolution)
    {
        var (workingWidth, workingHeight, scale) = LowPolyAbstractionSettings.GetWorkingSize(width, height, resolution);

        Assert.True(workingWidth >= LowPolyAbstractionSettings.MinimumWorkingSize);
        Assert.True(workingHeight >= LowPolyAbstractionSettings.MinimumWorkingSize);
        Assert.True(scale > 0f);
        Assert.True(workingWidth * scale >= width);
        Assert.True(workingHeight * scale >= height);
    }

    [Theory]
    [InlineData(1920, 1080, 1536, 1536, 864, 1.25f)]
    [InlineData(1080, 1920, 1536, 864, 1536, 1.25f)]
    [InlineData(100, 100, 2048, 100, 100, 1f)]
    [InlineData(8, 8, 1536, 16, 16, 1f)]
    [InlineData(4096, 16, 1024, 1024, 16, 4f)]
    public void TheLongSideIsReducedToTheResolutionButNotBelowTheMinimum(int width, int height, int resolution, int expectedWidth, int expectedHeight, float expectedScale)
    {
        var (workingWidth, workingHeight, scale) = LowPolyAbstractionSettings.GetWorkingSize(width, height, resolution);

        Assert.Equal(expectedWidth, workingWidth);
        Assert.Equal(expectedHeight, workingHeight);
        Assert.Equal(expectedScale, scale, 6);
    }

    [Theory]
    [InlineData(4096, 6144)]
    [InlineData(8192, 12288)]
    [InlineData(16384, 24576)]
    public void TheSiteCapacityLeavesRoomForRefinement(int baseSites, int expected)
        => Assert.Equal(expected, LowPolyAbstractionSettings.GetSiteCapacity(baseSites));

    [Theory]
    [InlineData(6144, 12304)]
    [InlineData(24576, 49168)]
    public void TheTriangleCapacityHoldsEveryTriangleOfTheSites(int siteCapacity, int expected)
        => Assert.Equal(expected, LowPolyAbstractionSettings.GetTriangleCapacity(siteCapacity));

    [Theory]
    [InlineData(-5f, 4096, 409)]
    [InlineData(0f, 4096, 409)]
    [InlineData(1f, 4096, 4096)]
    [InlineData(5f, 4096, 4096)]
    [InlineData(0f, 100, 64)]
    [InlineData(1f, 100, 100)]
    public void TheSiteCountStaysWithinItsRange(float detail, int baseSites, int expected)
        => Assert.Equal(expected, LowPolyAbstractionSettings.GetSiteCount(detail, baseSites));

    [Fact]
    public void MoreDetailPlacesMoreSites()
        => Assert.True(LowPolyAbstractionSettings.GetSiteCount(0.75f, 4096) > LowPolyAbstractionSettings.GetSiteCount(0.25f, 4096));

    [Fact]
    public void EveryQualityPlacesNoMoreSitesThanItsCapacity()
    {
        foreach (var quality in Enum.GetValues<LowPolyAbstractionQuality>())
        {
            var baseSites = LowPolyAbstractionSettings.GetQuality(quality).BaseSites;
            Assert.True(LowPolyAbstractionSettings.GetSiteCount(1f, baseSites) <= LowPolyAbstractionSettings.GetSiteCapacity(baseSites));
        }
    }

    [Theory]
    [InlineData(-1f, 81.92f)]
    [InlineData(0f, 81.92f)]
    [InlineData(1f, 24.576f)]
    [InlineData(2f, 24.576f)]
    public void TheEdgeSpacingStaysWithinItsRange(float fidelity, float expected)
        => Assert.Equal(expected, LowPolyAbstractionSettings.GetEdgeSampleSpacing(1024, 1024, fidelity), 3);

    [Fact]
    public void AHigherFidelitySamplesTheEdgesMoreDensely()
        => Assert.True(LowPolyAbstractionSettings.GetEdgeSampleSpacing(1024, 1024, 0.75f) < LowPolyAbstractionSettings.GetEdgeSampleSpacing(1024, 1024, 0.25f));

    [Fact]
    public void TheEdgeSpacingNeverDropsBelowTwoPixels()
        => Assert.Equal(2f, LowPolyAbstractionSettings.GetEdgeSampleSpacing(10, 10, 1f));

    [Theory]
    [InlineData(1024, 1024, 20.48f)]
    [InlineData(10, 10, 1f)]
    public void TheLaneWidthFollowsTheWorkingSizeButNotBelowOnePixel(int width, int height, float expected)
        => Assert.Equal(expected, LowPolyAbstractionSettings.GetLaneWidth(width, height), 4);

    [Theory]
    [InlineData(-1f, LowPolyAbstractionSettings.MaximumErrorThreshold)]
    [InlineData(0f, LowPolyAbstractionSettings.MaximumErrorThreshold)]
    [InlineData(1f, LowPolyAbstractionSettings.MinimumErrorThreshold)]
    [InlineData(2f, LowPolyAbstractionSettings.MinimumErrorThreshold)]
    public void TheErrorThresholdStaysWithinItsRange(float refine, float expected)
        => Assert.Equal(expected, LowPolyAbstractionSettings.GetErrorThreshold(refine), 7);

    [Fact]
    public void MoreRefinementSplitsTrianglesWithSmallerErrors()
        => Assert.True(LowPolyAbstractionSettings.GetErrorThreshold(0.75f) < LowPolyAbstractionSettings.GetErrorThreshold(0.25f));

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(1, 1, 1)]
    [InlineData(2, 2, 1)]
    [InlineData(3, 3, 2)]
    [InlineData(256, 128, 128)]
    [InlineData(257, 16, 256)]
    public void TheJumpFloodStartsFromHalfThePowerOfTwoCoveringTheLongSide(int width, int height, int expected)
        => Assert.Equal(expected, LowPolyAbstractionSettings.GetJumpFloodInitialStep(width, height));
}
