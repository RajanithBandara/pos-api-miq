using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Api.Authorization;
using POS.Application.Common.Models;
using POS.Application.Dashboard;

namespace POS.Api.Controllers.Dashboard;

/// <summary>
/// Administration performed from the dashboard, as opposed to the out-of-band provisioning key.
///
/// These routes need an administrator grant, so they are unreachable until at least one exists —
/// which is what the provisioning key is still for. Once the first administrator is in place,
/// this is how access is managed thereafter.
/// </summary>
[ApiController]
[Route("api/admin")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.Admin)]
public class AdminController : ControllerBase
{
    private readonly IDashboardAccessService _access;

    public AdminController(IDashboardAccessService access)
    {
        _access = access;
    }

    [HttpGet("dashboard-users")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DashboardUserDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDashboardUsers(CancellationToken cancellationToken)
    {
        var users = await _access.GetUsersAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<DashboardUserDto>>.Ok(users));
    }

    /// <summary>
    /// Pre-authorises an address. The person gets access the first time they sign in to Neon Auth
    /// with it, provided the address is verified there.
    /// </summary>
    [HttpPost("dashboard-users")]
    [ProducesResponseType(typeof(ApiResponse<DashboardUserDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GrantDashboardAccess(
        [FromBody] GrantDashboardAccessRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _access.GrantAsync(request, cancellationToken);

        if (result.IsFailure)
            return StatusCode(DashboardAccessResults.Map(result.ErrorCode),
                ApiResponse<object>.Fail(result.Error!, "Access not granted"));

        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<DashboardUserDto>.Ok(result.Value!, "Dashboard access granted."));
    }

    /// <summary>
    /// Withdraws or restores access. Takes effect on the caller's next request rather than on
    /// their next token, because the grant is read from the database on every call.
    /// </summary>
    // "action" is avoided as a route parameter name for the same reason ProvisioningController
    // avoids it: MVC reserves it as an ambient route value and such a route never matches.
    [HttpPost("dashboard-users/{userId:guid}/{userAction:regex(^(activate|deactivate)$)}")]
    [ProducesResponseType(typeof(ApiResponse<DashboardUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetDashboardUserState(
        Guid userId,
        string userAction,
        CancellationToken cancellationToken)
    {
        var activate = userAction.Equals("activate", StringComparison.OrdinalIgnoreCase);
        var result = await _access.SetActiveAsync(userId, activate, cancellationToken);

        if (result.IsFailure)
            return StatusCode(DashboardAccessResults.Map(result.ErrorCode),
                ApiResponse<object>.Fail(result.Error!, "Dashboard user not updated"));

        return Ok(ApiResponse<DashboardUserDto>.Ok(result.Value!,
            activate ? "Dashboard access restored." : "Dashboard access withdrawn."));
    }
}

/// <summary>
/// Shared by the two places dashboard grants can be managed from, so an outcome maps to the same
/// status code whether the caller held an admin grant or the provisioning key.
/// </summary>
internal static class DashboardAccessResults
{
    public static int Map(string? errorCode) => errorCode switch
    {
        DashboardAccessService.CodeDuplicateEmail => StatusCodes.Status409Conflict,
        DashboardAccessService.CodeNotFound => StatusCodes.Status404NotFound,
        DashboardAccessService.CodeStoreNotFound => StatusCodes.Status404NotFound,
        _ => StatusCodes.Status400BadRequest
    };
}
