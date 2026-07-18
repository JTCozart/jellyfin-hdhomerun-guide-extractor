using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HdhrEpg.Services;

/// <summary>
/// Downloads one or more HDHomeRun devices' own EPGs (via SiliconDust's cloud XMLTV API),
/// rewrites each so channel ids match that device's tuner lineup, merges them, and writes the
/// result to disk as a single XMLTV file. Mirrors the mapping approach of the original
/// HDHomeRunEPG-to-XmlTv Python script: the SiliconDust XMLTV feed uses its own internal
/// channel ids, so each lineup entry's guide number is matched to a channel by its
/// <c>display-name</c> and the id is substituted throughout the raw document.
/// </summary>
public sealed class HdhrEpgGenerator
{
    // A plain client with just a User-Agent set, used for both the device itself and
    // SiliconDust's cloud API. Deliberately not Jellyfin's shared IHttpClientFactory client:
    // some HDHomeRun devices' minimal embedded web servers return an empty body when sent
    // Jellyfin's default request headers (notably Accept-Encoding), even though the exact
    // same URL works fine from a browser or a bare client such as this one.
    private static readonly HttpClient DeviceClient = CreateClient(bypassCertValidation: false);

    // The SiliconDust XMLTV endpoint is fetched with certificate validation disabled, mirroring
    // the reference script's use of ssl._create_unverified_context() for this specific call.
    private static readonly HttpClient CloudClient = CreateClient(bypassCertValidation: true);

    private static HttpClient CreateClient(bool bypassCertValidation)
    {
        var handler = new HttpClientHandler();
        if (bypassCertValidation)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        var client = new HttpClient(handler);

        // HttpClient sends no User-Agent by default; some servers (SiliconDust's cloud API
        // included) reject or misbehave on requests without one.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HDHomeRunEPG-Jellyfin/1.0 (+https://github.com/JTCozart/jellyfin-hdhomerun-guide-extractor)");
        return client;
    }

    private static readonly Regex XmlDeclaration = new(@"^\s*<\?xml[^>]*>\s*", RegexOptions.Singleline | RegexOptions.Compiled);

    private readonly ILogger<HdhrEpgGenerator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HdhrEpgGenerator"/> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{T}"/> interface.</param>
    public HdhrEpgGenerator(ILogger<HdhrEpgGenerator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Fetches each device's EPG, remaps channel ids to its tuner lineup, merges the results
    /// (so channels shared across devices only appear once), and writes the merged XMLTV to
    /// <paramref name="outputPath"/>. A device that fails does not stop the others; the run
    /// only throws if every device fails.
    /// </summary>
    /// <param name="hosts">The HDHomeRun device hostnames or IP addresses.</param>
    /// <param name="outputPath">The XMLTV file path to write.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The merged channel/programme counts and any per-device failures.</returns>
    public async Task<EpgGenerationResult> GenerateAsync(IReadOnlyList<string> hosts, string outputPath, CancellationToken cancellationToken)
    {
        var distinctHosts = hosts
            .Select(h => h?.Trim() ?? string.Empty)
            .Where(h => h.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinctHosts.Count == 0)
        {
            throw new InvalidOperationException("No HDHomeRun host is configured.");
        }

        var succeeded = new List<(string Host, XDocument Document)>();
        var failures = new List<(string Host, string Error)>();

        foreach (var host in distinctHosts)
        {
            try
            {
                var document = await FetchDeviceDocumentAsync(host, cancellationToken).ConfigureAwait(false);
                succeeded.Add((host, document));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to fetch guide data from HDHomeRun device {Host}", host);
                failures.Add((host, ex.Message));
            }
        }

        if (succeeded.Count == 0)
        {
            throw new InvalidOperationException(
                "All configured HDHomeRun devices failed: " + string.Join("; ", failures.Select(f => $"{f.Host} ({f.Error})")));
        }

        var merged = MergeDocuments(succeeded);
        var channelCount = merged.Root?.Elements("channel").Count() ?? 0;
        var programCount = merged.Root?.Elements("programme").Count() ?? 0;

        await WriteAtomicAsync(outputPath, merged, cancellationToken).ConfigureAwait(false);

        return new EpgGenerationResult(channelCount, programCount, failures);
    }

    private async Task<XDocument> FetchDeviceDocumentAsync(string host, CancellationToken cancellationToken)
    {
        var deviceAuth = await DiscoverDeviceAuthAsync(host, cancellationToken).ConfigureAwait(false);
        var channels = await FetchLineupAsync(host, cancellationToken).ConfigureAwait(false);
        if (channels.Count == 0)
        {
            throw new InvalidOperationException($"The device's channel lineup is empty ({host}).");
        }

        var rawXml = await FetchCloudXmlTvAsync(deviceAuth, cancellationToken).ConfigureAwait(false);
        var remapped = RemapChannelIds(rawXml, channels);
        return XDocument.Parse(remapped);
    }

    /// <summary>
    /// Combines each device's already-remapped XMLTV document into one, keeping the first
    /// occurrence of any channel id. Devices deliberately sharing channels (e.g. redundant
    /// tuners on the same feed) are common, so later duplicates of a channel and its
    /// programmes are dropped rather than merged or overwritten.
    /// </summary>
    private static XDocument MergeDocuments(IReadOnlyList<(string Host, XDocument Document)> succeeded)
    {
        var firstRoot = succeeded[0].Document.Root ?? throw new InvalidOperationException("Guide document has no root element.");
        var mergedRoot = new XElement(firstRoot.Name, firstRoot.Attributes());
        var seenChannelIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (_, document) in succeeded)
        {
            if (document.Root is null)
            {
                continue;
            }

            var addedByThisDevice = new HashSet<string>(StringComparer.Ordinal);

            foreach (var channel in document.Root.Elements("channel"))
            {
                var id = channel.Attribute("id")?.Value;
                if (id is null || !seenChannelIds.Add(id))
                {
                    continue;
                }

                addedByThisDevice.Add(id);
                mergedRoot.Add(new XElement(channel));
            }

            foreach (var programme in document.Root.Elements("programme"))
            {
                var channelId = programme.Attribute("channel")?.Value;
                if (channelId is not null && addedByThisDevice.Contains(channelId))
                {
                    mergedRoot.Add(new XElement(programme));
                }
            }
        }

        return new XDocument(mergedRoot);
    }

    private async Task<string> DiscoverDeviceAuthAsync(string host, CancellationToken cancellationToken)
    {
        var url = $"http://{host}/discover.json";
        _logger.LogInformation("Fetching HDHomeRun device auth from {Url}", url);

        var json = await GetStringAsync(DeviceClient, url, "Fetching device info", cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException($"{url} returned an empty response.");
        }

        using var doc = JsonDocument.Parse(json);

        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (property.NameEquals("DeviceAuth") || property.Name.Contains("DeviceAuth", StringComparison.OrdinalIgnoreCase))
            {
                var auth = property.Value.GetString();
                if (!string.IsNullOrEmpty(auth))
                {
                    return auth;
                }
            }
        }

        throw new InvalidOperationException($"No DeviceAuth found in {url}. Is this a HDHomeRun device?");
    }

    private async Task<List<HdhrLineupChannel>> FetchLineupAsync(string host, CancellationToken cancellationToken)
    {
        var url = $"http://{host}/lineup.json";
        _logger.LogInformation("Fetching HDHomeRun channel lineup from {Url}", url);

        var json = await GetStringAsync(DeviceClient, url, "Fetching channel lineup", cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException($"{url} returned an empty response.");
        }

        var channels = JsonSerializer.Deserialize<List<HdhrLineupChannel>>(json);
        return channels ?? [];
    }

    private async Task<string> FetchCloudXmlTvAsync(string deviceAuth, CancellationToken cancellationToken)
    {
        var url = $"https://api.hdhomerun.com/api/xmltv?DeviceAuth={Uri.EscapeDataString(deviceAuth)}";
        _logger.LogInformation("Fetching HDHomeRun XMLTV guide data");

        string xml;
        try
        {
            using var response = await CloudClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            xml = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(DescribeHttpFailure("Fetching guide data from SiliconDust's cloud API", "https://api.hdhomerun.com/api/xmltv", ex), ex);
        }

        // Clean up quirks in SiliconDust's feed: strip any XML declaration (a fresh one is
        // written on output) and fix backslash-escaped quotes that occasionally leak through.
        xml = XmlDeclaration.Replace(xml, string.Empty);
        xml = xml.Replace("\\'", "'", StringComparison.Ordinal);
        xml = xml.Replace("\\\"", "\"", StringComparison.Ordinal);
        xml = xml.Replace("\\t", "\t", StringComparison.Ordinal);

        return xml;
    }

    private static async Task<string> GetStringAsync(HttpClient client, string url, string step, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(DescribeHttpFailure(step, url, ex), ex);
        }
    }

    private static string DescribeHttpFailure(string step, string url, HttpRequestException ex)
        => ex.StatusCode is { } status
            ? $"{step} failed: {url} returned {(int)status} {status}."
            : $"{step} failed: could not reach {url} ({ex.Message}).";

    /// <summary>
    /// Substitutes SiliconDust's internal channel ids for the device's own tuner guide
    /// numbers, so the resulting XMLTV lines up with Jellyfin's HDHomeRun tuner channels
    /// without any manual channel mapping.
    /// </summary>
    private static string RemapChannelIds(string rawXml, IReadOnlyList<HdhrLineupChannel> channels)
    {
        var lookupDocument = XDocument.Parse(rawXml);
        var result = rawXml;

        foreach (var channel in channels)
        {
            var guideNumber = channel.GuideNumber ?? string.Empty;
            if (guideNumber.Length == 0)
            {
                continue;
            }

            var sourceId = FindChannelIdByDisplayName(lookupDocument, guideNumber);
            if (sourceId is not null && !string.Equals(sourceId, guideNumber, StringComparison.Ordinal))
            {
                result = result.Replace(sourceId, guideNumber, StringComparison.Ordinal);
            }
        }

        return result;
    }

    private static string? FindChannelIdByDisplayName(XDocument document, string displayName)
    {
        if (document.Root is null)
        {
            return null;
        }

        foreach (var channel in document.Root.Elements("channel"))
        {
            foreach (var name in channel.Elements("display-name"))
            {
                if (string.Equals(name.Value?.Trim(), displayName.Trim(), StringComparison.Ordinal))
                {
                    return channel.Attribute("id")?.Value;
                }
            }
        }

        return null;
    }

    private static async Task WriteAtomicAsync(string outputPath, XDocument document, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = outputPath + ".tmp";
        var settings = new System.Xml.XmlWriterSettings
        {
            Indent = true,
            IndentChars = "\t",
            Encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Async = true,
        };

        await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        await using (var writer = System.Xml.XmlWriter.Create(fileStream, settings))
        {
            await document.WriteToAsync(writer, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, outputPath, overwrite: true);
    }
}
