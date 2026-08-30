using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Api.Authorization;
using POS.Application.Common.Models;
using POS.Application.Sync;

namespace POS.Api.Controllers;

/// <summary>
/// Where a till's queued events land. Every route here is terminal-authenticated, and the store
/// and terminal are taken from the token rather than the request body — a till cannot file
/// events against a store it does not belong to, whatever it claims.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.Terminal)]
public class SyncController : ControllerBase
{
    private readonly ISyncIngestService _ingest;

    public SyncController(ISyncIngestService ingest)
    {
        _ingest = ingest;
    }

    /// <summary>
    /// Accepts a batch of events. Answers per event rather than per batch, so a worker whose
    /// batch was partly rejected knows exactly which entries to retire and which to retry
    /// instead of resending everything or dropping everything.
    /// </summary>
    [HttpPost("push")]
    [ProducesResponseType(typeof(ApiResponse<SyncPushResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Push([FromBody] SyncPushRequest request, CancellationToken cancellationToken)
    {
        if (!TryIdentify(out var storeId, out var terminalId, out var failure))
            return failure!;

        var result = await _ingest.PushAsync(storeId, terminalId, request, cancellationToken);

        if (result.IsFailure)
            return BadRequest(ApiResponse<object>.Fail(result.Error!, "Push refused"));

        var pushed = result.Value!;
        var message = pushed.Rejected > 0
            ? $"{pushed.Accepted} accepted, {pushed.Duplicates} duplicate, {pushed.Rejected} rejected."
            : $"{pushed.Accepted} accepted, {pushed.Duplicates} duplicate.";

        return Ok(ApiResponse<SyncPushResponse>.Ok(pushed, message));
    }

    /// <summary>
    /// The store's events after a cursor, oldest first, with the caller's own excluded.
    ///
    /// This is what gives every till in a shop the whole store's history: a sale rung at
    /// counter 1 comes back down to counter 2, so a refund can be processed wherever the
    /// customer happens to be standing.
    /// </summary>
    [HttpGet("pull")]
    [ProducesResponseType(typeof(ApiResponse<SyncPullResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Pull(
        [FromQuery] long since = 0,
        [FromQuery] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryIdentify(out var storeId, out var terminalId, out var failure))
            return failure!;

        var result = await _ingest.PullAsync(storeId, terminalId, since, limit, cancellationToken);

        if (result.IsFailure)
            return BadRequest(ApiResponse<object>.Fail(result.Error!, "Pull refused"));

        return Ok(ApiResponse<SyncPullResponse>.Ok(result.Value!));
    }

    /// <summary>
    /// What the server holds for the caller. The till uses it to reconcile after an outage, and
    /// it doubles as an authenticated liveness probe.
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(ApiResponse<SyncStatusResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        if (!TryIdentify(out var storeId, out var terminalId, out var failure))
            return failure!;

        return Ok(ApiResponse<SyncStatusResponse>.Ok(
            await _ingest.GetStatusAsync(storeId, terminalId, cancellationToken)));
    }

    private bool TryIdentify(out Guid storeId, out Guid terminalId, out IActionResult? failure)
    {
        var store = User.GetStoreId();
        var terminal = User.GetTerminalId();

        if (store is null || terminal is null)
        {
            storeId = terminalId = Guid.Empty;
            failure = Unauthorized(ApiResponse<object>.Fail(
                "This token does not identify a store and terminal.", "Unauthorized"));
            return false;
        }

        storeId = store.Value;
        terminalId = terminal.Value;
        failure = null;
        return true;
    }
}
