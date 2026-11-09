using DisplayKnob.Core.Displays;

namespace DisplayKnob.Core.Tests.Displays;

public sealed class MonitorIdentityTests
{
    private static readonly EdidInfo s_edid = new("DEL", 0x4123, 0, "ABC123", "DELL U2723QE");

    [Fact]
    public void Create_WithEdidAndPath_CombinesBoth()
    {
        var id = MonitorIdentity.Create(@"\\?\display#del4123#5&1&uid4353#{e6f07b5f}", s_edid, @"\\.\DISPLAY1");

        Assert.Equal(@"DEL4123-ABC123|\\?\DISPLAY#DEL4123#5&1&UID4353#{E6F07B5F}", id.Key);
        Assert.Equal("DEL4123-ABC123", id.EdidKey);
    }

    [Fact]
    public void Create_PathCaseDiffers_SameKey()
    {
        var a = MonitorIdentity.Create(@"\\?\DISPLAY#DEL4123#X", s_edid, "f");
        var b = MonitorIdentity.Create(@"\\?\display#del4123#x", s_edid, "f");

        Assert.Equal(a.Key, b.Key);
    }

    [Fact]
    public void Create_NoEdidNoPath_UsesFallback()
    {
        var id = MonitorIdentity.Create(null, null, @"\\.\DISPLAY2#0");

        Assert.Equal(@"no-edid|\\.\DISPLAY2#0", id.Key);
        Assert.Null(id.EdidKey);
        Assert.Null(id.DevicePath);
    }
}
