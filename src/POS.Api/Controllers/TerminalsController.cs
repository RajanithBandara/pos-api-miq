using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Application.Common.Models;
using POS.Application.Terminals;
using POS.Api.Authorization;

namespace POS.Api.Controllers;

/// <summary>
/// Endpoints a till calls for itself. Enrollment and token exchange are necessarily
/// unauthenticated — the caller has no token yet — so both are rate-sensitive and both
/// answer failures without revealing what exists.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class TerminalsController : ControllerBase
{
    private readonly ITerminalEnrollmentService _enrollment;

    public TerminalsController(ITerminalEnrollmentService enrollment)
    {
        _enrollment = enrollment;
    }

    /// <summary>
    /// Exchanges the durable device secret for a short-lived bearer token. Every sync call
    /// carries the token, never the secret.
    /// </summary>
    [HttpPost("token")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<TerminalTokenResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Token([FromBody] TerminalTokenRequest request, CancellationToken cancellationToken)
    {
        var result = await _enrollment.IssueTokenAsync(request, cancellationToken);

        if (result.IsFailure)
            return Unauthorized(ApiResponse<object>.Fail(result.Error!, "Authentication failed"));

        return Ok(ApiResponse<TerminalTokenResponse>.Ok(result.Value!, "Token issued."));
    }

    /// <summary>
    /// Returns the calling terminal's own record. Doubles as the till's proof that its token
    /// is still good and it has not been revoked.
    /// </summary>
    [HttpGet("me")]
    [Authorize(Policy = AuthorizationPolicies.Terminal)]
    [ProducesResponseType(typeof(ApiResponse<TerminalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var terminalId = User.GetTerminalId();
        if (terminalId is null)
            return Unauthorized(ApiResponse<object>.Fail("This token does not identify a terminal.", "Unauthorized"));

        var result = await _enrollment.GetByIdAsync(terminalId.Value, cancellationToken);

        // The token is signed and unexpired but its terminal is gone, so the row was deleted
        // after issuance. Treat it as an invalid credential rather than a missing resource.
        if (result.IsFailure)
            return Unauthorized(ApiResponse<object>.Fail("This terminal is no longer enrolled.", "Unauthorized"));

        return Ok(ApiResponse<TerminalDto>.Ok(result.Value!));
    }
}
