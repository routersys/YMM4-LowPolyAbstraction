namespace LowPolyAbstraction.Tests;

public sealed class LowPolyAbstractionEffectTests
{
    private static double ValueAt(YukkuriMovieMaker.Commons.Animation animation) => animation.GetValue(0, 1, 30);

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
}
