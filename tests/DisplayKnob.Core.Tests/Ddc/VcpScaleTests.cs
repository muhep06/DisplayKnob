using DisplayKnob.Core.Ddc;

namespace DisplayKnob.Core.Tests.Ddc;

public sealed class VcpScaleTests
{
    [Theory]
    [InlineData(0, 0u, 50u, 0u)]
    [InlineData(30, 0u, 50u, 15u)]
    [InlineData(100, 0u, 50u, 50u)]
    [InlineData(50, 10u, 30u, 20u)]
    [InlineData(150, 0u, 100u, 100u)]
    [InlineData(-5, 0u, 100u, 0u)]
    public void ToRaw_MapsPercentOntoRange(int percent, uint min, uint max, uint expected) =>
        Assert.Equal(expected, VcpScale.ToRaw(percent, min, max));

    [Theory]
    [InlineData(15u, 0u, 50u, 30)]
    [InlineData(5u, 10u, 30u, 0)]
    [InlineData(40u, 10u, 30u, 100)]
    [InlineData(7u, 5u, 5u, 0)]
    public void ToPercent_MapsRawOntoScale(uint raw, uint min, uint max, int expected) =>
        Assert.Equal(expected, VcpScale.ToPercent(raw, min, max));

    [Theory]
    [InlineData(0, 0u)]
    [InlineData(10, 1u)] // 1% of the range: finer than linear's 10.
    [InlineData(50, 25u)]
    [InlineData(100, 100u)]
    public void ToRaw_Squared_TakesFinerStepsAtTheBottom(int percent, uint expected) =>
        Assert.Equal(expected, VcpScale.ToRaw(percent, 0, 100, exponent: 2.0));

    [Theory]
    [InlineData(0u, 0)]
    [InlineData(1u, 10)]
    [InlineData(25u, 50)]
    [InlineData(100u, 100)]
    public void ToPercent_Squared_IsTheInverse(uint raw, int expected) =>
        Assert.Equal(expected, VcpScale.ToPercent(raw, 0, 100, exponent: 2.0));

    [Fact]
    public void ToRaw_Squared_RespectsTheUsableRange() =>
        Assert.Equal(30u, VcpScale.ToRaw(50, 10, 90, exponent: 2.0)); // 10 + 0.25 * 80

    [Fact]
    public void ToRaw_AndToPercent_AreMonotonicForACurve()
    {
        var raws = Enumerable.Range(0, 101).Select(p => VcpScale.ToRaw(p, 0, 100, exponent: 2.0)).ToList();
        var percents = Enumerable.Range(0, 101).Select(r => VcpScale.ToPercent((uint)r, 0, 100, exponent: 2.0)).ToList();

        Assert.Equal(raws.Order(), raws);
        Assert.Equal(percents.Order(), percents);
    }
}
