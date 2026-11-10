using DisplayKnob.Core.Input;
using DisplayKnob.Core.Settings;

namespace DisplayKnob.Core.Tests.Input;

public sealed class HotkeyMatcherTests
{
    private const ushort Up = 0x26;
    private const ushort Down = 0x28;
    private const ushort M = 0x4D;
    private const HotkeyModifiers CtrlAlt = HotkeyModifiers.Control | HotkeyModifiers.Alt;

    private static readonly HotkeyBindings s_ctrlAlt = new HotkeySettings().Resolve()!;

    [Theory]
    [InlineData(Up, VolumeKey.Up)]
    [InlineData(Down, VolumeKey.Down)]
    [InlineData(M, VolumeKey.Mute)]
    public void TryMatch_ExactModifiers_ReturnsTheAction(ushort vk, VolumeKey expected)
    {
        var matched = HotkeyMatcher.TryMatch(s_ctrlAlt, vk, CtrlAlt, altGr: false, out var key, out var fine);

        Assert.True(matched);
        Assert.Equal(expected, key);
        Assert.False(fine);
    }

    [Fact]
    public void TryMatch_ExtraWinKey_DoesNotMatch()
    {
        var matched = HotkeyMatcher.TryMatch(s_ctrlAlt, Up, CtrlAlt | HotkeyModifiers.Win, altGr: false, out _, out _);

        Assert.False(matched);
    }

    [Fact]
    public void TryMatch_MissingAlt_DoesNotMatch()
    {
        var matched = HotkeyMatcher.TryMatch(s_ctrlAlt, Up, HotkeyModifiers.Control, altGr: false, out _, out _);

        Assert.False(matched);
    }

    [Fact]
    public void TryMatch_UnboundKey_DoesNotMatch()
    {
        var matched = HotkeyMatcher.TryMatch(s_ctrlAlt, 0x41, CtrlAlt, altGr: false, out _, out _);

        Assert.False(matched);
    }

    [Fact]
    public void TryMatch_ExtraShift_MatchesAsFineStep()
    {
        var matched = HotkeyMatcher.TryMatch(s_ctrlAlt, Down, CtrlAlt | HotkeyModifiers.Shift, altGr: false, out var key, out var fine);

        Assert.True(matched);
        Assert.Equal(VolumeKey.Down, key);
        Assert.True(fine);
    }

    [Fact]
    public void TryMatch_ShiftIsPartOfTheBinding_IsNotAFineStep()
    {
        var bindings = new HotkeyBindings(new Hotkey(CtrlAlt | HotkeyModifiers.Shift, Up), null, null);

        var matched = HotkeyMatcher.TryMatch(bindings, Up, CtrlAlt | HotkeyModifiers.Shift, altGr: false, out _, out var fine);

        Assert.True(matched);
        Assert.False(fine);
    }

    [Fact]
    public void TryMatch_ShiftRequiredButNotHeld_DoesNotMatch()
    {
        var bindings = new HotkeyBindings(new Hotkey(CtrlAlt | HotkeyModifiers.Shift, Up), null, null);

        var matched = HotkeyMatcher.TryMatch(bindings, Up, CtrlAlt, altGr: false, out _, out _);

        Assert.False(matched);
    }

    [Fact]
    public void TryMatch_AltGr_DoesNotMatch()
    {
        // AltGr+Q types '@' on many layouts; Ctrl+Alt shortcuts must never swallow that kind of typing.
        var matched = HotkeyMatcher.TryMatch(s_ctrlAlt, Up, CtrlAlt, altGr: true, out _, out _);

        Assert.False(matched);
    }

    [Fact]
    public void TryMatch_UnassignedBinding_DoesNotMatch()
    {
        var bindings = new HotkeyBindings(null, new Hotkey(CtrlAlt, Down), null);

        var matched = HotkeyMatcher.TryMatch(bindings, Up, CtrlAlt, altGr: false, out _, out _);

        Assert.False(matched);
    }
}
