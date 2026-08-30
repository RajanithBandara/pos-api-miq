using System;
using System.Security.Claims;
using POS.Infrastructure.Authentication;

namespace POS.Api.Authorization;

public static class AuthorizationPolicies
{
    /// <summary>
    /// Requires a token minted for a till. Dashboard-user tokens will carry a different
    /// token_kind, so they cannot reach terminal endpoints just by being validly signed.
    /// </summary>
    public const string Terminal = "TerminalOnly";
}

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetTerminalId(this ClaimsPrincipal principal) =>
        ReadGuid(principal, TerminalClaims.TerminalId);

    public static Guid? GetStoreId(this ClaimsPrincipal principal) =>
        ReadGuid(principal, TerminalClaims.StoreId);

    public static Guid? GetTerminalUid(this ClaimsPrincipal principal) =>
        ReadGuid(principal, TerminalClaims.TerminalUid);

    private static Guid? ReadGuid(ClaimsPrincipal principal, string claimType)
    {
        var raw = principal.FindFirst(claimType)?.Value;
        return Guid.TryParse(raw, out var value) ? value : null;
    }
}
