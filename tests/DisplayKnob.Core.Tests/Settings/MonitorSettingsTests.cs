using DisplayKnob.Core.Settings;

namespace DisplayKnob.Core.Tests.Settings;

public sealed class MonitorSettingsTests
{
    [Fact]
    public void GetVolumeRange_Defaults_UseTheWholeReportedRange()
    {
        var range = new MonitorSettings().GetVolumeRange(100);

        Assert.Equal((0u, 100u), range);
    }

    [Theory]
    [InlineData(100, 60, 60u)]
    [InlineData(100, 10, 10u)]
    [InlineData(63, 50, 32u)] // 31.5 rounds away from zero.
    [InlineData(255, 40, 102u)]
    public void GetVolumeRange_MaxVolumePercent_LimitsTheUpperEnd(uint reportedMax, int percent, uint expectedMax)
    {
        var range = new MonitorSettings { MaxVolumePercent = percent }.GetVolumeRange(reportedMax);

        Assert.Equal((0u, expectedMax), range);
    }

    [Theory]
    [InlineData(0, 10u)] // Below the minimum percent: treated as 10%.
    [InlineData(-5, 10u)]
    [InlineData(250, 100u)] // Above 100%: treated as 100%.
    public void GetVolumeRange_PercentOutOfBounds_IsClamped(int percent, uint expectedMax)
    {
        var range = new MonitorSettings { MaxVolumePercent = percent }.GetVolumeRange(100);

        Assert.Equal(expectedMax, range.Max);
    }

    [Fact]
    public void GetVolumeRange_TinyReportedRange_KeepsAtLeastOneStep()
    {
        var range = new MonitorSettings { MaxVolumePercent = 10 }.GetVolumeRange(3);

        Assert.Equal(1u, range.Max);
    }

    [Theory]
    [InlineData(40u, 60, 40u)] // The raw limit is the lower one.
    [InlineData(90u, 60, 60u)] // The percentage limit is the lower one.
    public void GetVolumeRange_RawAndPercentLimits_TheLowerWins(uint rawMax, int percent, uint expectedMax)
    {
        var range = new MonitorSettings { VolumeMax = rawMax, MaxVolumePercent = percent }.GetVolumeRange(100);

        Assert.Equal(expectedMax, range.Max);
    }

    [Fact]
    public void GetVolumeRange_MinAboveTheLimit_IsPulledDown()
    {
        var range = new MonitorSettings { VolumeMin = 80, MaxVolumePercent = 50 }.GetVolumeRange(100);

        Assert.Equal((50u, 50u), range);
    }

    [Fact]
    public void GetVolumeRange_NothingReported_IsEmpty()
    {
        var range = new MonitorSettings { MaxVolumePercent = 50 }.GetVolumeRange(0);

        Assert.Equal((0u, 0u), range);
    }
}
