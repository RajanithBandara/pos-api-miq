using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using POS.Application.Common.Models;

namespace POS.Api.Authentication;

/// <summary>
/// Guards the provisioning endpoints with a shared key supplied out of band.
///
/// This is a bootstrap credential, deliberately: creating stores and minting enrollment
/// codes has to be possible before any dashboard user exists, in the same way a cluster join
/// token works. It is a stand-in for real administrator authentication, not a replacement
/// for it — when dashboard users land, these endpoints move behind a role and this attribute
/// goes away.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireProvisioningKeyAttribute : Attribute, IAsyncAuthorizationFilter
{
    public const string HeaderName = "X-Provisioning-Key";
    public const string ConfigurationKey = "Provisioning:ApiKey";

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger<RequireProvisioningKeyAttribute>();

        var expected = configuration[ConfigurationKey];

        // Fail closed. An unset key means provisioning is unavailable, never that it is open:
        // a deployment that forgot to configure one must not silently expose store creation.
        if (string.IsNullOrWhiteSpace(expected))
        {
            logger.LogError("Provisioning refused: {ConfigurationKey} is not configured on this deployment.", ConfigurationKey);
            context.Result = new ObjectResult(ApiResponse<object>.Fail(
                "Provisioning is not configured on this server.", "Provisioning unavailable"))
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
            return Task.CompletedTask;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var provided)
            || !FixedTimeEquals(provided.ToString(), expected))
        {
            logger.LogWarning("Provisioning refused for {Path} from {RemoteIp}: missing or incorrect key.",
                context.HttpContext.Request.Path,
                context.HttpContext.Connection.RemoteIpAddress);

            context.Result = new ObjectResult(ApiResponse<object>.Fail(
                $"A valid {HeaderName} header is required.", "Unauthorized"))
            {
                StatusCode = StatusCodes.Status401Unauthorized
            };
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Compares in time independent of how many leading characters match, so the response
    /// latency cannot be used to recover the key one character at a time.
    /// </summary>
    private static bool FixedTimeEquals(string? provided, string expected)
    {
        if (string.IsNullOrEmpty(provided)) return false;

        var a = Encoding.UTF8.GetBytes(provided);
        var b = Encoding.UTF8.GetBytes(expected);

        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
