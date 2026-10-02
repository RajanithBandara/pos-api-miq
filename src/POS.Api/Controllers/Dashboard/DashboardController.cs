using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Api.Authorization;
using POS.Application.Common.Models;
using POS.Application.Dashboard;

namespace POS.Api.Controllers.Dashboard;

/// <summary>
/// What the Angular dashboard reads.
///
/// Every route here is authenticated by Neon Auth and authorised against a grant held in this
/// API's own database. The caller's store scope comes from that grant and is passed to the
/// application layer explicitly — nothing on these routes takes a store from the request, so
/// there is no parameter with which a user could widen what they are allowed to see.
/// </summary>
[ApiController]
[Route("api/dashboard")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.Web)]
public class DashboardController : ControllerBase
{
    private readonly IDashboardQueryService _dashboard;

    public DashboardController(IDashboardQueryService dashboard)
    {
        _dashboard = dashboard;
    }

    /// <summary>
    /// The caller's own identity as this API resolved it: which grant matched, what role it
    /// carries, and which store it is confined to. The dashboard uses it to decide what to render
    /// — though every one of those decisions is enforced again server-side.
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<DashboardIdentityResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        if (User.GetDashboardIdentity() is not { } identity)
            return Forbid();

        return Ok(ApiResponse<DashboardIdentityResponse>.Ok(
            await _dashboard.GetIdentityAsync(identity, cancellationToken)));
    }

    /// <summary>
    /// Stores, terminals and sync activity across whatever the caller is allowed to see.
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(ApiResponse<DashboardSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        if (User.GetDashboardIdentity() is not { } identity)
            return Forbid();

        return Ok(ApiResponse<DashboardSummaryResponse>.Ok(
            await _dashboard.GetSummaryAsync(identity, cancellationToken)));
    }
}
