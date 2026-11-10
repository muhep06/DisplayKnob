using DisplayKnob.Core.Input;

namespace DisplayKnob.Core.Tests.Input;

public sealed class HotkeyTests
{
    [Theory]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x26, "Ctrl+Alt+Up")]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Win, 0x4D, "Ctrl+Alt+Win+M")]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x21, "Ctrl+Shift+PgUp")]
    [InlineData(HotkeyModifiers.Alt, 0x70, "Alt+F1")]
    [InlineData(HotkeyModifiers.Control, 0x6B, "Ctrl+Num+")]
    [InlineData(HotkeyModifiers.Control, 0x35, "Ctrl+5")]
    [InlineData(HotkeyModifiers.Control, 0xBA, "Ctrl+Key 0xBA")]
    public void ToString_FormatsModifiersThenKey(HotkeyModifiers modifiers, int vk, string expected)
    {
        var hotkey = new Hotkey(modifiers, (ushort)vk);

        Assert.Equal(expected, hotkey.ToString());
    }

    [Fact]
    public void HasSystemModifier_ShiftOnly_IsFalse()
    {
        Assert.False(new Hotkey(HotkeyModifiers.Shift, 0x41).HasSystemModifier);
        Assert.True(new Hotkey(HotkeyModifiers.Win, 0x41).HasSystemModifier);
    }
}
