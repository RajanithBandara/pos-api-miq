using Microsoft.AspNetCore.Mvc;
using POS.Application.Common.Models;

namespace POS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public abstract class ApiControllerBase : ControllerBase
{
    protected IActionResult HandleResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(ApiResponse<T>.Ok(result.Value!));
        }

        return result.ErrorCode switch
        {
            "NOT_FOUND" => NotFound(ApiResponse<T>.Fail(result.Error ?? "Resource not found.")),
            "UNAUTHORIZED" or "AUTH_INVALID_CREDENTIALS" => Unauthorized(ApiResponse<T>.Fail(result.Error ?? "Unauthorized.")),
            "FORBIDDEN" => StatusCode(403, ApiResponse<T>.Fail(result.Error ?? "Forbidden.")),
            "DUPLICATE_ENTITY" or "DUPLICATE_SKU" or "DUPLICATE_USERNAME" or "DUPLICATE_CODE" or "DUPLICATE_CUSTOMER" or "ALREADY_VOIDED" =>
                Conflict(ApiResponse<T>.Fail(result.Error ?? "Conflict.")),
            _ => BadRequest(ApiResponse<T>.Fail(result.Error ?? "Bad request."))
        };
    }

    protected IActionResult HandleResult(Result result)
    {
        if (result.IsSuccess)
        {
            return Ok(ApiResponse<object>.Ok(new { success = true }));
        }

        return result.ErrorCode switch
        {
            "NOT_FOUND" => NotFound(ApiResponse<object>.Fail(result.Error ?? "Resource not found.")),
            "UNAUTHORIZED" => Unauthorized(ApiResponse<object>.Fail(result.Error ?? "Unauthorized.")),
            "FORBIDDEN" => StatusCode(403, ApiResponse<object>.Fail(result.Error ?? "Forbidden.")),
            "ALREADY_REVOKED" => Conflict(ApiResponse<object>.Fail(result.Error ?? "Conflict.")),
            _ => BadRequest(ApiResponse<object>.Fail(result.Error ?? "Bad request."))
        };
    }
}
