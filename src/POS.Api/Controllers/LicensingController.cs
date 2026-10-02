using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using POS.Application.Common.Models;
using POS.Application.Licensing;

namespace POS.Api.Controllers;

/// <summary>
/// Endpoints for validating MarchIQ product license keys and activating stores / register terminals.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class LicensingController : ControllerBase
{
    private readonly ILicensingService _licensing;

    public LicensingController(ILicensingService licensing)
    {
        _licensing = licensing;
    }

    /// <summary>
    /// Validates a product license key and returns the plan details, store binding, and terminal limits.
    /// Used by the POS first-time setup wizard to automatically detect the shop and plan.
    /// </summary>
    [HttpPost("validate-key")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<ValidateProductKeyResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ValidateKey(
        [FromBody] ValidateProductKeyRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _licensing.ValidateKeyAsync(request.ProductKey, cancellationToken);

        if (result.IsFailure)
            return BadRequest(ApiResponse<object>.Fail(result.Error!, "License key validation failed"));

        return Ok(ApiResponse<ValidateProductKeyResponse>.Ok(result.Value!, "License key is valid."));
    }

    /// <summary>
    /// Activates a license key. If the store does not exist, registers the store and enrolls the initial terminal.
    /// If the store is already registered, enrolls an additional terminal under the license terminal limit.
    /// </summary>
    [HttpPost("activate")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<ActivateStoreLicenseResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Activate(
        [FromBody] ActivateStoreLicenseRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _licensing.ActivateAsync(request, cancellationToken);

        if (result.IsFailure)
            return BadRequest(ApiResponse<object>.Fail(result.Error!, "License activation failed"));

        return Ok(ApiResponse<ActivateStoreLicenseResponse>.Ok(
            result.Value!,
            "Store and terminal successfully activated with product license."));
    }
}
