using System;

namespace Jellyfin.Plugin.HdhrEpg.Services;

/// <summary>
/// Tracks the outcome of the most recent guide generation attempts, so the config page and
/// API can report live status without re-running anything.
/// </summary>
public sealed class EpgStatus
{
    private readonly object _lock = new();

    private DateTime? _lastRunUtc;
    private DateTime? _lastSuccessUtc;
    private string? _lastError;
    private int _channelCount;
    private int _programCount;
    private bool _isRunning;

    /// <summary>
    /// Gets the UTC time of the last attempted refresh, successful or not.
    /// </summary>
    public DateTime? LastRunUtc { get { lock (_lock) { return _lastRunUtc; } } }

    /// <summary>
    /// Gets the UTC time of the last successful refresh.
    /// </summary>
    public DateTime? LastSuccessUtc { get { lock (_lock) { return _lastSuccessUtc; } } }

    /// <summary>
    /// Gets the error message from the last failed refresh, or null if the last refresh succeeded.
    /// </summary>
    public string? LastError { get { lock (_lock) { return _lastError; } } }

    /// <summary>
    /// Gets the number of channels written in the last successful refresh.
    /// </summary>
    public int ChannelCount { get { lock (_lock) { return _channelCount; } } }

    /// <summary>
    /// Gets the number of programmes written in the last successful refresh.
    /// </summary>
    public int ProgramCount { get { lock (_lock) { return _programCount; } } }

    /// <summary>
    /// Gets a value indicating whether a refresh is currently in progress.
    /// </summary>
    public bool IsRunning { get { lock (_lock) { return _isRunning; } } }

    /// <summary>
    /// Marks a refresh as started.
    /// </summary>
    public void MarkStarted()
    {
        lock (_lock)
        {
            _isRunning = true;
        }
    }

    /// <summary>
    /// Records the outcome of a refresh.
    /// </summary>
    /// <param name="success">Whether the refresh succeeded.</param>
    /// <param name="error">The error message, when it did not.</param>
    /// <param name="channelCount">The number of channels written.</param>
    /// <param name="programCount">The number of programmes written.</param>
    public void MarkFinished(bool success, string? error, int channelCount, int programCount)
    {
        lock (_lock)
        {
            _isRunning = false;
            _lastRunUtc = DateTime.UtcNow;
            if (success)
            {
                _lastSuccessUtc = _lastRunUtc;
                _lastError = null;
                _channelCount = channelCount;
                _programCount = programCount;
            }
            else
            {
                _lastError = error;
            }
        }
    }
}
