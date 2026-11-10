using DisplayKnob.Core.Input;
using DisplayKnob.Core.Settings;
using Serilog.Core;

namespace DisplayKnob.Core.Tests.Settings;

public sealed class HotkeySettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "DisplayKnobTests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Resolve_Default_IsCtrlAltUpDownM()
    {
        var bindings = new HotkeySettings().Resolve();

        Assert.NotNull(bindings);
        Assert.Equal("Ctrl+Alt+Up", bindings.Up?.ToString());
        Assert.Equal("Ctrl+Alt+Down", bindings.Down?.ToString());
        Assert.Equal("Ctrl+Alt+M", bindings.Mute?.ToString());
    }

    [Fact]
    public void Resolve_CtrlAltWinPreset_AddsTheWinKey()
    {
        var bindings = new HotkeySettings { Preset = HotkeyPreset.CtrlAltWin }.Resolve();

        Assert.Equal("Ctrl+Alt+Win+Up", bindings?.Up?.ToString());
    }

    [Fact]
    public void Resolve_Disabled_ReturnsNull()
    {
        Assert.Null(new HotkeySettings { Enabled = false }.Resolve());
    }

    [Fact]
    public void Resolve_CustomWithOneAssigned_LeavesTheRestUnassigned()
    {
        var up = new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x21);
        var settings = new HotkeySettings { Preset = HotkeyPreset.Custom, CustomUp = up };

        var bindings = settings.Resolve();

        Assert.Equal(up, bindings?.Up);
        Assert.Null(bindings?.Down);
        Assert.Null(bindings?.Mute);
    }

    [Fact]
    public void Update_Hotkeys_SurviveAReload()
    {
        var up = new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x21);
        var store = new JsonSettingsStore(SettingsPath, Logger.None);

        store.Update(s => s with { Hotkeys = new HotkeySettings { Preset = HotkeyPreset.Custom, CustomUp = up, Enabled = false } });
        var reloaded = new JsonSettingsStore(SettingsPath, Logger.None).Current.Hotkeys;

        Assert.False(reloaded.Enabled);
        Assert.Equal(HotkeyPreset.Custom, reloaded.Preset);
        Assert.Equal(up, reloaded.CustomUp);
        Assert.Null(reloaded.CustomDown);
    }

    [Fact]
    public void Constructor_FileWithoutHotkeys_UsesTheDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, """{ "volumeStep": 5 }""");

        var settings = new JsonSettingsStore(SettingsPath, Logger.None).Current;

        Assert.Equal(5, settings.VolumeStep);
        Assert.True(settings.Hotkeys.Enabled);
        Assert.Equal(HotkeyPreset.CtrlAlt, settings.Hotkeys.Preset);
    }
}
