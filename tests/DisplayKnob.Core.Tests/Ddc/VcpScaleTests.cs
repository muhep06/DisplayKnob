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
}
