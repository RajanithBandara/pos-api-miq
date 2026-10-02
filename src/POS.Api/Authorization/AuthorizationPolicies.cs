using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using POS.Api.Authentication;
using POS.Application.Dashboard;
using POS.Domain.Enums;
using POS.Infrastructure.Authentication;

namespace POS.Api.Authorization;

public static class AuthorizationPolicies
{
    /// <summary>
    /// Requires a token minted for a till. Dashboard tokens carry a different token_kind, so they
    /// cannot reach terminal endpoints just by being validly signed — and they are not even
    /// validated here, because the policy pins the terminal scheme.
    /// </summary>
    public const string Terminal = "TerminalOnly";

    /// <summary>An authenticated Angular user with an active grant in this API's database.</summary>
    public const string Web = "WebPolicy";

    /// <summary>The same, plus the administrator role.</summary>
    public const string Admin = "AdminPolicy";
}

public static class AuthorizationExtensions
{
    /// <summary>
    /// Every policy names the scheme that may satisfy it.
    ///
    /// That pinning is the real separation between the two clients, and it does the work before
    /// any claim is examined: a dashboard token presented to a sync endpoint is handed to the
    /// terminal handler, which cannot verify a signature made with a key it does not have, and
    /// the request is refused as unauthenticated. The claim checks below are a second, independent
    /// barrier rather than the only one.
    /// </summary>
    public static IServiceCollection AddApiAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthorizationPolicies.Terminal, policy => policy
                .AddAuthenticationSchemes(AuthenticationSchemes.Pos)
                .RequireAuthenticatedUser()
                .RequireClaim(TerminalClaims.TokenKind, TerminalClaims.TerminalTokenKind));

            // token_kind is added by this API after the Neon subject was found in our own
            // database, never copied from the token. Requiring it is therefore a check that the
            // caller was provisioned here, not merely that they signed in to Neon Auth.
            options.AddPolicy(AuthorizationPolicies.Web, policy => policy
                .AddAuthenticationSchemes(AuthenticationSchemes.Neon)
                .RequireAuthenticatedUser()
                .RequireClaim(DashboardClaims.TokenKind, DashboardClaims.DashboardTokenKind));

            options.AddPolicy(AuthorizationPolicies.Admin, policy => policy
                .AddAuthenticationSchemes(AuthenticationSchemes.Neon)
                .RequireAuthenticatedUser()
                .RequireClaim(DashboardClaims.TokenKind, DashboardClaims.DashboardTokenKind)
                .RequireRole(nameof(DashboardRole.Admin)));
        });

        return services;
    }
}

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetTerminalId(this ClaimsPrincipal principal) =>
        ReadGuid(principal, TerminalClaims.TerminalId);

    public static Guid? GetStoreId(this ClaimsPrincipal principal) =>
        ReadGuid(principal, TerminalClaims.StoreId);

    public static Guid? GetTerminalUid(this ClaimsPrincipal principal) =>
        ReadGuid(principal, TerminalClaims.TerminalUid);

    /// <summary>
    /// Rebuilds the dashboard identity the authentication handler established, so controllers can
    /// hand it to the application layer without looking anything up again.
    ///
    /// Returns null unless every claim this API added is present, which is only the case for a
    /// caller that reached here through the dashboard policies. A controller that forgets its
    /// [Authorize] therefore fails to find an identity rather than quietly running unscoped.
    /// </summary>
    public static DashboardIdentity? GetDashboardIdentity(this ClaimsPrincipal principal)
    {
        if (principal.FindFirst(DashboardClaims.TokenKind)?.Value != DashboardClaims.DashboardTokenKind)
            return null;

        var userId = ReadGuid(principal, DashboardClaims.DashboardUserId);
        var subject = principal.FindFirst(DashboardClaims.Subject)?.Value;

        if (userId is null || string.IsNullOrWhiteSpace(subject))
            return null;

        if (!Enum.TryParse<DashboardRole>(principal.FindFirst(ClaimTypes.Role)?.Value, out var role))
            return null;

        return new DashboardIdentity(
            userId.Value,
            subject,
            principal.FindFirst(DashboardClaims.Email)?.Value ?? string.Empty,
            principal.FindFirst(DashboardClaims.Name)?.Value,
            role,
            ReadGuid(principal, DashboardClaims.StoreScope));
    }

    private static Guid? ReadGuid(ClaimsPrincipal principal, string claimType)
    {
        var raw = principal.FindFirst(claimType)?.Value;
        return Guid.TryParse(raw, out var value) ? value : null;
    }
}
