using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.HdhrEpg.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        Devices = new List<HdhrDeviceConfig>();
        RefreshIntervalHours = 12;
    }

    /// <summary>
    /// Gets or sets the configured HDHomeRun devices. Their guides are merged into a single
    /// XMLTV file, so channels shared across devices (e.g. redundant tuners on the same feed)
    /// only appear once.
    /// </summary>
    public List<HdhrDeviceConfig> Devices { get; set; }

    /// <summary>
    /// Gets or sets how often, in hours, the guide is regenerated from the devices.
    /// </summary>
    public int RefreshIntervalHours { get; set; }
}
