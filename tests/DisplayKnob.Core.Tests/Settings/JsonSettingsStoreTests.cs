using DisplayKnob.Core.Settings;
using Serilog.Core;

namespace DisplayKnob.Core.Tests.Settings;

public sealed class JsonSettingsStoreTests : IDisposable
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
    public void Constructor_NoFile_UsesDefaults()
    {
        var store = new JsonSettingsStore(SettingsPath, Logger.None);

        Assert.Equal(DisplayKnobSettings.Default, store.Current);
        Assert.True(store.Current.PinEndpointVolume);
    }

    [Fact]
    public void Update_PersistsAcrossInstances()
    {
        var store = new JsonSettingsStore(SettingsPath, Logger.None);

        store.Update(s => s with
        {
            PinEndpointVolume = false,
            EndpointMonitorOverrides = new Dictionary<string, string> { ["{endpoint-1}"] = "DEL4123-ABC|PATH" },
        });
        var reloaded = new JsonSettingsStore(SettingsPath, Logger.None);

        Assert.False(reloaded.Current.PinEndpointVolume);
        Assert.Equal("DEL4123-ABC|PATH", reloaded.Current.EndpointMonitorOverrides["{endpoint-1}"]);
    }

    [Fact]
    public void Update_RaisesChanged()
    {
        var store = new JsonSettingsStore(SettingsPath, Logger.None);
        var raised = 0;
        store.Changed += (_, _) => raised++;

        store.Update(s => s with { PinEndpointVolume = false });

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Update_MaxVolumePercent_PersistsAcrossInstances()
    {
        var store = new JsonSettingsStore(SettingsPath, Logger.None);

        store.Update(s => s with { Monitors = new Dictionary<string, MonitorSettings> { ["DEL4123-ABC|PATH"] = new() { MaxVolumePercent = 55 } } });
        var reloaded = new JsonSettingsStore(SettingsPath, Logger.None);

        Assert.Equal(55, reloaded.Current.ForMonitor("DEL4123-ABC|PATH").MaxVolumePercent);
    }

    [Fact]
    public void Constructor_MonitorEntryWithoutMaxVolumePercent_UsesFullRange()
    {
        // A file saved before the option existed.
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, """{ "monitors": { "DEL4123-ABC|PATH": { "ddcVolumeEnabled": false } } }""");

        var monitor = new JsonSettingsStore(SettingsPath, Logger.None).Current.ForMonitor("DEL4123-ABC|PATH");

        Assert.False(monitor.DdcVolumeEnabled);
        Assert.Equal(100, monitor.MaxVolumePercent);
    }

    [Fact]
    public void Constructor_CorruptFile_UsesDefaultsAndKeepsBackup()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{ not json");

        var store = new JsonSettingsStore(SettingsPath, Logger.None);

        Assert.Equal(DisplayKnobSettings.Default, store.Current);
        Assert.True(File.Exists(SettingsPath + ".bad"));
    }
}
