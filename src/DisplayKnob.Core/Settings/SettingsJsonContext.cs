using System.Text.Json.Serialization;

namespace DisplayKnob.Core.Settings;

/// <summary>
/// Source-generated JSON metadata for settings (no reflection).
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DisplayKnobSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
