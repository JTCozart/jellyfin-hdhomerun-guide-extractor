using System;
using System.Collections.Generic;
using Jellyfin.Plugin.HdhrEpg.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.HdhrEpg;

/// <summary>
/// The main plugin entry point.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "HDHomeRun EPG";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("561f5530-ec1f-4bb0-9dd4-1efbff63ed87");

    /// <inheritdoc />
    public override string Description =>
        "Generates an auto-refreshing XMLTV guide from a HDHomeRun device's own EPG, with channel IDs pre-mapped to the tuner lineup.";

    /// <summary>
    /// Gets the full path to the generated XMLTV file.
    /// </summary>
    public string XmlTvFilePath => System.IO.Path.Combine(DataFolderPath, "epg.xml");

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = "hdhrepg",
            DisplayName = "HDHomeRun EPG",
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html",
            EnableInMainMenu = true,
            MenuIcon = "live_tv",
        };
    }
}
