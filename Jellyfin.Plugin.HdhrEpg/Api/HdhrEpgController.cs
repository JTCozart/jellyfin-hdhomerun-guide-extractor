using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HdhrEpg.Services;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.HdhrEpg.Api;

/// <summary>
/// REST API for the HDHomeRun EPG plugin: status reporting and an on-demand refresh.
/// </summary>
[ApiController]
[Route("HdhrEpg")]
[Produces("application/json")]
public class HdhrEpgController : ControllerBase
{
    private readonly EpgRefreshService _refreshService;
    private readonly EpgStatus _status;

    /// <summary>
    /// Initializes a new instance of the <see cref="HdhrEpgController"/> class.
    /// </summary>
    /// <param name="refreshService">Instance of the <see cref="EpgRefreshService"/> class.</param>
    /// <param name="status">Instance of the <see cref="EpgStatus"/> class.</param>
    public HdhrEpgController(EpgRefreshService refreshService, EpgStatus status)
    {
        _refreshService = refreshService;
        _status = status;
    }

    /// <summary>
    /// Gets the current refresh status and the file path to give Jellyfin's built-in XMLTV
    /// guide provider.
    /// </summary>
    /// <returns>The current status.</returns>
    [HttpGet("Status")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetStatus()
    {
        var config = Plugin.Instance?.Configuration;
        return Ok(new
        {
            Devices = config?.Devices,
            RefreshIntervalHours = config?.RefreshIntervalHours,
            FilePath = Plugin.Instance?.XmlTvFilePath,
            IsRunning = _status.IsRunning,
            LastRunUtc = _status.LastRunUtc,
            LastSuccessUtc = _status.LastSuccessUtc,
            LastError = _status.LastError,
            ChannelCount = _status.ChannelCount,
            ProgramCount = _status.ProgramCount,
        });
    }

    /// <summary>
    /// Triggers an immediate guide refresh from the configured HDHomeRun device.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The status after the refresh attempt.</returns>
    [HttpPost("Refresh")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<object>> Refresh(CancellationToken cancellationToken)
    {
        try
        {
            await _refreshService.RefreshNowAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = ex.Message });
        }

        return GetStatus();
    }
}
