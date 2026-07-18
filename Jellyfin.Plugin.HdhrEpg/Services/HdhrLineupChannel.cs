using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.HdhrEpg.Services;

/// <summary>
/// A single entry from the HDHomeRun device's <c>/lineup.json</c> response. Only the fields
/// needed for channel-id mapping are modeled; the rest of the payload is ignored.
/// </summary>
public sealed class HdhrLineupChannel
{
    /// <summary>
    /// Gets or sets the tuner-assigned guide number (e.g. "4.1"), used as the XMLTV channel id.
    /// </summary>
    [JsonPropertyName("GuideNumber")]
    public string GuideNumber { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the channel's display name, as reported by the tuner.
    /// </summary>
    [JsonPropertyName("GuideName")]
    public string? GuideName { get; set; }
}
