using DisplayKnob.Core.Audio;

namespace DisplayKnob.Core.Tests.Audio;

public sealed class EndpointMonitorMatcherTests
{
    [Fact]
    public void Match_EndpointContainsMonitorName_ReturnsKey()
    {
        var key = EndpointMonitorMatcher.Match(
            "S34C65xU (NVIDIA High Definition Audio)",
            [("samsung", "S34C65xU"), ("dell", "DELL U2723QE")]);

        Assert.Equal("samsung", key);
    }

    [Fact]
    public void Match_CaseDiffers_StillMatches()
    {
        var key = EndpointMonitorMatcher.Match("s34c65xu (Intel Display Audio)", [("samsung", "S34C65xU")]);

        Assert.Equal("samsung", key);
    }

    [Fact]
    public void Match_NoMonitorName_ReturnsNull()
    {
        var key = EndpointMonitorMatcher.Match("Headphones (Realtek(R) Audio)", [("samsung", "S34C65xU")]);

        Assert.Null(key);
    }

    [Fact]
    public void Match_TwoIdenticalModels_IsAmbiguous()
    {
        var key = EndpointMonitorMatcher.Match("S34C65xU (NVIDIA High Definition Audio)", [("a", "S34C65xU"), ("b", "S34C65xU")]);

        Assert.Null(key);
    }

    [Fact]
    public void Match_NestedNames_PrefersLongest()
    {
        var key = EndpointMonitorMatcher.Match("DELL U2723QE (AMD High Definition Audio)", [("short", "DELL"), ("long", "DELL U2723QE")]);

        Assert.Equal("long", key);
    }

    [Fact]
    public void Match_EmptyMonitorName_IsIgnored()
    {
        var key = EndpointMonitorMatcher.Match("Speakers (Realtek(R) Audio)", [("blank", "")]);

        Assert.Null(key);
    }
}
