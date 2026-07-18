using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HdhrEpg.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HdhrEpg.Services;

/// <summary>
/// Background service that periodically regenerates the XMLTV guide from the configured
/// HDHomeRun device, so the file Jellyfin's built-in XMLTV guide provider reads stays fresh
/// without any manual intervention.
/// </summary>
public sealed class EpgRefreshService : IHostedService, IDisposable
{
    private readonly HdhrEpgGenerator _generator;
    private readonly EpgStatus _status;
    private readonly ILogger<EpgRefreshService> _logger;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="EpgRefreshService"/> class.
    /// </summary>
    /// <param name="generator">Instance of the <see cref="HdhrEpgGenerator"/> class.</param>
    /// <param name="status">Instance of the <see cref="EpgStatus"/> class.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{T}"/> interface.</param>
    public EpgRefreshService(HdhrEpgGenerator generator, EpgStatus status, ILogger<EpgRefreshService> logger)
    {
        _generator = generator;
        _status = status;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();
        _loopTask = RunLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();
        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    /// <summary>
    /// Runs the refresh immediately, outside the periodic schedule. Used by the "Refresh now"
    /// button and the API.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the refresh finishes.</returns>
    public async Task RefreshNowAsync(CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        var hosts = (config?.Devices ?? new List<HdhrDeviceConfig>())
            .Select(d => d.Host)
            .ToList();
        var outputPath = Plugin.Instance?.XmlTvFilePath ?? throw new InvalidOperationException("Plugin is not initialized.");

        _status.MarkStarted();
        try
        {
            var result = await _generator.GenerateAsync(hosts, outputPath, cancellationToken).ConfigureAwait(false);
            string? warning = result.Failures.Count > 0
                ? "Some devices failed: " + string.Join("; ", result.Failures.Select(f => $"{f.Host} ({f.Error})"))
                : null;

            _status.MarkFinished(success: true, error: warning, result.ChannelCount, result.ProgramCount);
            if (warning is not null)
            {
                _logger.LogWarning("HDHomeRun EPG refreshed with warnings: {Warning}", warning);
            }

            _logger.LogInformation("HDHomeRun EPG refreshed: {Channels} channels, {Programs} programmes", result.ChannelCount, result.ProgramCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _status.MarkFinished(success: false, error: ex.Message, channelCount: 0, programCount: 0);
            _logger.LogError(ex, "HDHomeRun EPG refresh failed");
            throw;
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RefreshNowAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Already recorded on _status; keep the loop alive so the next scheduled
                // attempt still runs (a transient device/network issue shouldn't stop
                // refreshing permanently).
            }

            var hours = Math.Clamp(Plugin.Instance?.Configuration.RefreshIntervalHours ?? 12, 1, 168);
            try
            {
                await Task.Delay(TimeSpan.FromHours(hours), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cts?.Dispose();
    }
}
