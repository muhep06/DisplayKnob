using System.Text.Json;
using Serilog;

namespace DisplayKnob.Core.Settings;

/// <summary>
/// <see cref="ISettingsStore"/> backed by a JSON file. A missing or corrupt file yields defaults;
/// a corrupt file is kept aside as <c>.bad</c> so the user's data isn't silently lost.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _path;
    private readonly ILogger _logger;
    private readonly Lock _lock = new();
    private DisplayKnobSettings _current;

    /// <summary>Loads settings from <paramref name="path"/>.</summary>
    public JsonSettingsStore(string path, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(logger);

        _path = path;
        _logger = logger.ForContext<JsonSettingsStore>();
        _current = Load();
    }

    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <summary>The default settings file: <c>%AppData%\DisplayKnob\settings.json</c>.</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DisplayKnob",
        "settings.json");

    /// <inheritdoc/>
    public DisplayKnobSettings Current => Volatile.Read(ref _current);

    /// <inheritdoc/>
    public void Update(Func<DisplayKnobSettings, DisplayKnobSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        lock (_lock)
        {
            var updated = change(_current);
            if (updated == _current)
            {
                return;
            }

            Volatile.Write(ref _current, updated);
            Save(updated);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private DisplayKnobSettings Load()
    {
        if (!File.Exists(_path))
        {
            return DisplayKnobSettings.Default;
        }

        try
        {
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.DisplayKnobSettings) ?? DisplayKnobSettings.Default;
        }
        catch (JsonException ex)
        {
            var backup = _path + ".bad";
            _logger.Warning(ex, "Settings file is corrupt; using defaults. The old file was moved to {Backup}", backup);
            TryMove(_path, backup);
            return DisplayKnobSettings.Default;
        }
        catch (IOException ex)
        {
            _logger.Warning(ex, "Could not read settings from {Path}; using defaults", _path);
            return DisplayKnobSettings.Default;
        }
    }

    private void Save(DisplayKnobSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!); // _path is a full file path, so it has a directory.

            // Write to a temp file and swap it in, so a crash mid-write can't corrupt the settings.
            var temp = _path + ".tmp";
            using (var stream = File.Create(temp))
            {
                JsonSerializer.Serialize(stream, settings, SettingsJsonContext.Default.DisplayKnobSettings);
            }

            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The in-memory settings still apply for this session.
            _logger.Error(ex, "Could not save settings to {Path}", _path);
        }
    }

    private void TryMove(string from, string to)
    {
        try
        {
            File.Move(from, to, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(ex, "Could not move {From} to {To}", from, to);
        }
    }
}
