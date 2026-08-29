using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Application.Common;
using POS.Application.Common.Models;
using POS.Application.Synchronization.DTOs;
using POS.Application.Synchronization.Services;

namespace POS.Api.Controllers;

[Authorize(Policy = Permissions.SynchronizeData)]
public class SyncController : ApiControllerBase
{
    private readonly ISyncEngineService _syncEngineService;

    public SyncController(ISyncEngineService syncEngineService)
    {
        _syncEngineService = syncEngineService;
    }

    /// <summary>
    /// Pushes a batch of locally generated transactions/changes from the offline-first POS terminal to the server.
    /// This endpoint is idempotent and safe to retry on network failures.
    /// </summary>
    [HttpPost("push")]
    [ProducesResponseType(typeof(ApiResponse<SyncPushResponseDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> PushSyncBatch(
        [FromBody] SyncPushRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _syncEngineService.ProcessPushBatchAsync(request, cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Pulls incremental change log updates (Products, Categories, Prices, Permissions) from the server since the terminal's last synchronized version.
    /// </summary>
    [HttpGet("pull")]
    [ProducesResponseType(typeof(ApiResponse<SyncPullResponseDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> PullSyncChanges(
        [FromQuery] Guid storeId,
        [FromQuery] Guid posTerminalId,
        [FromQuery] long lastSyncVersion = 0,
        [FromQuery] int batchSize = 250,
        CancellationToken cancellationToken = default)
    {
        var request = new SyncPullRequestDto(storeId, posTerminalId, lastSyncVersion, batchSize);
        var result = await _syncEngineService.ProcessPullChangesAsync(request, cancellationToken);
        return HandleResult(result);
    }
}
