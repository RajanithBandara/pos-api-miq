using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using POS.Api.Authentication;
using POS.Api.Controllers.Dashboard;
using POS.Application.Common.Models;
using POS.Application.Dashboard;
using POS.Application.Provisioning;
using POS.Application.Terminals;

namespace POS.Api.Controllers;

/// <summary>
/// Store and terminal administration. Guarded by the bootstrap provisioning key until
/// dashboard user accounts exist, at which point these move behind an administrator role.
/// </summary>
[ApiController]
[Route("api/provisioning")]
[Produces("application/json")]
[RequireProvisioningKey]
public class ProvisioningController : ControllerBase
{
    private readonly IProvisioningService _provisioning;

    public ProvisioningController(IProvisioningService provisioning)
    {
        _provisioning = provisioning;
    }

    [HttpPost("stores")]
    [ProducesResponseType(typeof(ApiResponse<StoreDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateStore([FromBody] CreateStoreRequest request, CancellationToken cancellationToken)
    {
        var result = await _provisioning.CreateStoreAsync(request, cancellationToken);

        if (result.IsFailure)
            return StatusCode(Map(result.ErrorCode), ApiResponse<object>.Fail(result.Error!, "Store not created"));

        return CreatedAtAction(nameof(GetStores), null,
            ApiResponse<StoreDto>.Ok(result.Value!, "Store created."));
    }

    [HttpGet("stores")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<StoreDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStores([FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var stores = await _provisioning.GetStoresAsync(includeInactive, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<StoreDto>>.Ok(stores));
    }

    /// <summary>
    /// Mints a single-use code an installer types into a till. Short-lived by default so a
    /// code left on screen stops working on its own.
    /// </summary>
    [HttpPost("stores/{storeId:guid}/enrollment-codes")]
    [ProducesResponseType(typeof(ApiResponse<EnrollmentCodeDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> IssueEnrollmentCode(
        Guid storeId,
        [FromBody] IssueEnrollmentCodeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _provisioning.IssueEnrollmentCodeAsync(storeId, request, cancellationToken);

        if (result.IsFailure)
            return StatusCode(Map(result.ErrorCode), ApiResponse<object>.Fail(result.Error!, "Code not issued"));

        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<EnrollmentCodeDto>.Ok(result.Value!, "Enrollment code issued."));
    }

    [HttpGet("stores/{storeId:guid}/enrollment-codes")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EnrollmentCodeDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEnrollmentCodes(Guid storeId, CancellationToken cancellationToken)
    {
        var result = await _provisioning.GetEnrollmentCodesAsync(storeId, cancellationToken);

        if (result.IsFailure)
            return StatusCode(Map(result.ErrorCode), ApiResponse<object>.Fail(result.Error!, "Store not found"));

        return Ok(ApiResponse<IReadOnlyList<EnrollmentCodeDto>>.Ok(result.Value!));
    }

    [HttpGet("terminals")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TerminalDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTerminals([FromQuery] Guid? storeId = null, CancellationToken cancellationToken = default)
    {
        var terminals = await _provisioning.GetTerminalsAsync(storeId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<TerminalDto>>.Ok(terminals));
    }

    /// <summary>
    /// Applies suspend, reactivate or revoke. Revocation is permanent and clears the stored
    /// credential; the terminal row stays because synced events still point at it.
    /// </summary>
    // The route parameter is deliberately not called "action": MVC reserves that name as an
    // ambient route value for the action method itself, and a route that uses it never matches.
    [HttpPost("terminals/{terminalId:guid}/{terminalAction:regex(^(suspend|reactivate|revoke)$)}")]
    [ProducesResponseType(typeof(ApiResponse<TerminalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetTerminalStatus(Guid terminalId, string terminalAction, CancellationToken cancellationToken)
    {
        var result = await _provisioning.SetTerminalStatusAsync(terminalId, terminalAction, cancellationToken);

        if (result.IsFailure)
            return StatusCode(Map(result.ErrorCode), ApiResponse<object>.Fail(result.Error!, "Terminal not updated"));

        return Ok(ApiResponse<TerminalDto>.Ok(result.Value!, $"Terminal {terminalAction.ToLowerInvariant()}d."));
    }

    /// <summary>
    /// Creates the first dashboard administrator.
    ///
    /// This exists because of a genuine ordering problem, not as a convenience: the admin
    /// endpoints need an administrator grant, and until one exists there is nobody who can create
    /// one. The provisioning key breaks that cycle exactly once, in the same way it already
    /// bootstraps the first store. Everything afterwards belongs on /api/admin/dashboard-users.
    /// </summary>
    [HttpPost("dashboard-users")]
    [ProducesResponseType(typeof(ApiResponse<DashboardUserDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GrantDashboardAccess(
        [FromBody] GrantDashboardAccessRequest request,
        [FromServices] IDashboardAccessService access,
        CancellationToken cancellationToken)
    {
        var result = await access.GrantAsync(request, cancellationToken);

        if (result.IsFailure)
            return StatusCode(DashboardAccessResults.Map(result.ErrorCode),
                ApiResponse<object>.Fail(result.Error!, "Access not granted"));

        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<DashboardUserDto>.Ok(result.Value!, "Dashboard access granted."));
    }

    [HttpGet("dashboard-users")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DashboardUserDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDashboardUsers(
        [FromServices] IDashboardAccessService access,
        CancellationToken cancellationToken)
    {
        var users = await access.GetUsersAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<DashboardUserDto>>.Ok(users));
    }

    private static int Map(string? errorCode) => errorCode switch
    {
        ProvisioningService.CodeNotFound => StatusCodes.Status404NotFound,
        ProvisioningService.CodeDuplicateStore => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest
    };
}
