using System.Text.Json.Serialization;

namespace DisplayKnob.Core.Settings;

/// <summary>Where the on-screen volume indicator appears while the volume group is active.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GroupOsdMode>))]
public enum GroupOsdMode
{
    /// <summary>One indicator on every monitor of the group, each showing its own level.</summary>
    EachMonitor,

    /// <summary>One indicator, on the monitor the audio plays through.</summary>
    TargetOnly,
}
