using System;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using POS.Application.Dashboard;
using POS.Infrastructure.Authentication;

namespace POS.Api.Authentication;

/// <summary>
/// Configures the <see cref="AuthenticationSchemes.Neon"/> bearer handler.
///
/// A configuration class rather than a lambda because it needs services — an HTTP client factory
/// for the JWKS fetch, the environment to decide how strict to be about TLS, the bound Neon
/// options — and the AddJwtBearer callback has no way to resolve any of them.
/// </summary>
internal sealed class ConfigureNeonAuthJwtBearerOptions(
    IOptions<NeonAuthOptions> neonOptions,
    IHttpClientFactory httpClientFactory,
    IHostEnvironment environment,
    ILoggerFactory loggerFactory) : IConfigureNamedOptions<JwtBearerOptions>
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<ConfigureNeonAuthJwtBearerOptions>();

    public void Configure(JwtBearerOptions options)
    {
        // Only ever meaningful for a named scheme. This overload exists to satisfy the
        // interface and must not configure anything.
    }

    public void Configure(string? name, JwtBearerOptions options)
    {
        if (!string.Equals(name, AuthenticationSchemes.Neon, StringComparison.Ordinal))
            return;

        var neon = neonOptions.Value;

        // Neon claims are read by their own names. Without this, inbound claim mapping renames
        // "sub" to a WS-Federation URI and the subject lookup silently finds nothing — which
        // presents as every dashboard user being unauthorised, with no error anywhere.
        options.MapInboundClaims = false;

        // The token is verified on arrival and the identity is carried in claims; keeping the
        // raw token in the authentication properties would only widen where a bearer credential
        // can be read from.
        options.SaveToken = false;

        if (!neon.IsConfigured)
        {
            ConfigureAsUnavailable(options);
            return;
        }

        options.RequireHttpsMetadata = !environment.IsDevelopment();

        options.TokenValidationParameters = new TokenValidationParameters
        {
            // Neon signs with the origin of the auth URL, not its full path.
            ValidateIssuer = true,
            ValidIssuer = neon.ResolvedIssuer,

            ValidateAudience = true,
            ValidAudience = neon.ResolvedAudience,

            ValidateLifetime = true,
            RequireExpirationTime = true,

            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,

            // Pinning the algorithm is what stops a token signed with something weaker — or with
            // "none" — from being offered against whatever key happens to be present. Neon signs
            // with EdDSA; nothing else is accepted here.
            ValidAlgorithms = [EdDsaSecurityKey.Algorithm],

            // Neon access tokens live fifteen minutes, so a generous skew would be a large
            // fraction of the whole lifetime.
            ClockSkew = TimeSpan.FromSeconds(Math.Clamp(neon.ClockSkewSeconds, 0, 300)),

            NameClaimType = DashboardClaims.Email,

            // Roles come from this API's own database and are added below as ClaimTypes.Role.
            // The Neon "role" claim is the Postgres RLS role ("authenticated") and says nothing
            // about what its holder may do here; leaving the role claim type at the default keeps
            // it from ever being mistaken for one.
            RoleClaimType = ClaimTypes.Role
        };

        // Key retrieval, caching, scheduled refresh and — the part that matters — re-fetching
        // when a token arrives with an unknown key id all come from the framework's configuration
        // manager. Neon rotates its signing keys, and hand-rolled caching is how a rotation turns
        // into an outage.
        options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            neon.ResolvedJwksUri,
            new NeonAuthJwksRetriever(loggerFactory.CreateLogger<NeonAuthJwksRetriever>()),
            new HttpDocumentRetriever(httpClientFactory.CreateClient(NeonAuthDefaults.HttpClientName))
            {
                // Verification keys are public, but fetching them over plaintext would let anyone
                // on the path substitute their own and mint tokens this API would then trust.
                RequireHttps = !environment.IsDevelopment()
            });

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = OnTokenValidatedAsync,
            OnAuthenticationFailed = context =>
            {
                _logger.LogWarning(context.Exception,
                    "Neon Auth token rejected for {Path}: {Reason}",
                    context.HttpContext.Request.Path, context.Exception.Message);
                return Task.CompletedTask;
            }
        };

        _logger.LogInformation(
            "Neon Auth configured. Issuer {Issuer}, JWKS {Jwks}.",
            neon.ResolvedIssuer, neon.ResolvedJwksUri);
    }

    /// <summary>
    /// A deployment with no Neon Auth configuration refuses every dashboard request.
    ///
    /// Fails closed for the same reason the provisioning key does: a missing setting must mean
    /// the feature is unavailable, never that it is unguarded. Without this the handler would
    /// reject tokens anyway for want of keys, but with an error that reads like a token problem
    /// rather than a configuration one.
    /// </summary>
    private void ConfigureAsUnavailable(JwtBearerOptions options)
    {
        _logger.LogError(
            "{Section}:BaseUrl is not configured. Dashboard authentication will refuse every request.",
            NeonAuthOptions.SectionName);

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            IssuerSigningKeys = []
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                context.Fail(new InvalidOperationException(
                    "Neon Auth is not configured on this deployment."));
                return Task.CompletedTask;
            }
        };
    }

    /// <summary>
    /// Turns a verified Neon subject into an authorised dashboard user, or leaves it merely
    /// authenticated.
    ///
    /// The token has proved who signed in to Neon Auth. It has not shown that this API knows
    /// them, and it cannot: anyone can sign up. So the subject is looked up in our own database
    /// and the claims that policies actually test are added only when a grant is found. A caller
    /// with a valid token and no grant reaches the endpoint authenticated and unauthorised, which
    /// the authorization middleware answers with 403 rather than 401 — the honest distinction
    /// between "I do not know you" and "I know you, and the answer is no".
    /// </summary>
    private static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        var services = context.HttpContext.RequestServices;
        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger<ConfigureNeonAuthJwtBearerOptions>();

        if (context.Principal?.Identities.FirstOrDefault() is not { } identity)
        {
            context.Fail(new SecurityTokenException("The token produced no identity."));
            return;
        }

        // Nothing from the token is allowed to arrive under a claim name this API assigns
        // meaning to. The Neon payload includes the user's own profile fields, and a claim named
        // token_kind or store_scope reaching a policy check would let someone grant themselves
        // access by editing their profile.
        StripReservedClaims(identity);

        var subject = identity.FindFirst(DashboardClaims.Subject)?.Value;
        if (string.IsNullOrWhiteSpace(subject))
        {
            context.Fail(new SecurityTokenException("The token carries no subject."));
            return;
        }

        var principal = new NeonAuthPrincipal(
            subject,
            identity.FindFirst(DashboardClaims.Email)?.Value,
            ReadBoolean(identity, DashboardClaims.EmailVerified),
            ReadBoolean(identity, DashboardClaims.Banned),
            identity.FindFirst(DashboardClaims.Name)?.Value);

        var directory = services.GetRequiredService<IDashboardUserDirectory>();
        var resolved = await directory.ResolveAsync(principal, context.HttpContext.RequestAborted);

        if (resolved is null)
        {
            // Deliberately not a failure. The credential is good; the caller simply has no
            // access here, and the policies will say so.
            return;
        }

        identity.AddClaim(new Claim(DashboardClaims.TokenKind, DashboardClaims.DashboardTokenKind));
        identity.AddClaim(new Claim(DashboardClaims.DashboardUserId, resolved.UserId.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Role, resolved.Role.ToString()));

        if (resolved.StoreScope is Guid storeId)
            identity.AddClaim(new Claim(DashboardClaims.StoreScope, storeId.ToString()));

        logger.LogDebug("Dashboard user {UserId} authenticated as {Role}.", resolved.UserId, resolved.Role);
    }

    private static void StripReservedClaims(ClaimsIdentity identity)
    {
        string[] reserved =
        [
            DashboardClaims.TokenKind,
            DashboardClaims.DashboardUserId,
            DashboardClaims.StoreScope,
            ClaimTypes.Role
        ];

        foreach (var type in reserved)
        {
            foreach (var claim in identity.FindAll(type).ToList())
                identity.TryRemoveClaim(claim);
        }
    }

    /// <summary>
    /// Reads a JSON boolean claim. Absent or unparseable reads as false, which is the safe answer
    /// for both claims this is used on: an unverified address cannot claim a grant, and an
    /// unreadable ban flag must not be treated as a ban.
    /// </summary>
    private static bool ReadBoolean(ClaimsIdentity identity, string claimType) =>
        bool.TryParse(identity.FindFirst(claimType)?.Value, out var value) && value;
}
