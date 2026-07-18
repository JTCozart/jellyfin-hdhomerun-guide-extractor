namespace Jellyfin.Plugin.HdhrEpg.Configuration;

/// <summary>
/// A single configured HDHomeRun device.
/// </summary>
public class HdhrDeviceConfig
{
    /// <summary>
    /// Gets or sets the HDHomeRun device hostname or IP address.
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional friendly name for the device, shown on the config page only.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}
