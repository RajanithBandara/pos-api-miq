using System.Diagnostics.CodeAnalysis;

namespace POS.Infrastructure.Authentication;

/// <summary>
/// Where the Angular dashboard's tokens come from, and what this API will accept as proof of
/// one.
///
/// Neon Auth (Managed Better Auth) hands the browser a short-lived EdDSA-signed JWT and
/// publishes the public keys that verify it. Everything here is derived from the one URL Neon
/// gives you, so a deployment only has to configure <see cref="BaseUrl"/>; the overrides exist
/// for the case where Neon changes a path and this API should not need a new release to follow.
/// </summary>
public sealed class NeonAuthOptions
{
    public const string SectionName = "NeonAuth";

    /// <summary>
    /// The Neon Auth URL for the project, e.g.
    /// <c>https://ep-something.aws.neon.tech/neondb/auth</c>. Empty means this deployment has no
    /// dashboard authentication configured, and every dashboard request is refused.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Overrides the derived JWKS address. Normally left unset.</summary>
    public string? JwksUri { get; set; }

    /// <summary>Overrides the derived issuer. Normally left unset.</summary>
    public string? Issuer { get; set; }

    /// <summary>Overrides the derived audience. Normally left unset.</summary>
    public string? Audience { get; set; }

    /// <summary>
    /// Tolerance for clock drift between this server and Neon's. Small on purpose: Neon's
    /// access tokens only live fifteen minutes, so a generous skew is a meaningful fraction of
    /// the token's whole lifetime.
    /// </summary>
    public int ClockSkewSeconds { get; set; } = 60;

    /// <summary>
    /// True when there is enough configuration to verify a token. Anything less and the
    /// authentication handler refuses every request rather than falling back to something
    /// weaker — an unconfigured deployment must be closed, not open.
    /// </summary>
    [MemberNotNullWhen(true, nameof(ResolvedJwksUri), nameof(ResolvedIssuer), nameof(ResolvedAudience))]
    public bool IsConfigured => ResolvedJwksUri is not null
                                && ResolvedIssuer is not null
                                && ResolvedAudience is not null;

    /// <summary>
    /// <c>&lt;base&gt;/.well-known/jwks.json</c>, the standard location Neon documents. These are
    /// public verification keys, so fetching them needs no credential — but it does need TLS,
    /// which is enforced where the retriever is built.
    /// </summary>
    public string? ResolvedJwksUri =>
        !string.IsNullOrWhiteSpace(JwksUri)
            ? JwksUri.Trim()
            : BaseUri is { } uri ? uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/.well-known/jwks.json" : null;

    /// <summary>
    /// Neon signs with the <em>origin</em> of the auth URL, not the full path: an auth URL of
    /// <c>https://host/neondb/auth</c> issues tokens whose <c>iss</c> is <c>https://host</c>.
    /// Getting this wrong fails every token with an unhelpful "issuer invalid", so it is derived
    /// rather than typed by hand.
    /// </summary>
    public string? ResolvedIssuer =>
        !string.IsNullOrWhiteSpace(Issuer)
            ? Issuer.Trim()
            : BaseUri?.GetLeftPart(UriPartial.Authority);

    /// <summary>Neon sets <c>aud</c> to the same origin as <c>iss</c>.</summary>
    public string? ResolvedAudience =>
        !string.IsNullOrWhiteSpace(Audience)
            ? Audience.Trim()
            : BaseUri?.GetLeftPart(UriPartial.Authority);

    private Uri? BaseUri =>
        Uri.TryCreate(BaseUrl?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri
            : null;
}

/// <summary>
/// Claim names on the dashboard side, kept beside the terminal ones for the same reason: a typo
/// on either side of a claim shows up as an unexplained 403 rather than as an error.
/// </summary>
public static class DashboardClaims
{
    /// <summary>Same claim type the terminal tokens use, with a different value.</summary>
    public const string TokenKind = TerminalClaims.TokenKind;

    /// <summary>
    /// Marks a principal as a dashboard user this API has actually authorised. It is added by
    /// this API after looking the Neon subject up in our own database — never read from the
    /// incoming token — so its presence means "provisioned here", not merely "signed in".
    /// </summary>
    public const string DashboardTokenKind = "dashboard";

    public const string DashboardUserId = "dashboard_user_id";

    /// <summary>The store this user is confined to. Absent when the grant covers every store.</summary>
    public const string StoreScope = "store_scope";

    // Claims Neon Auth itself puts in the token. Only the subject is trusted for identity, and
    // none of them are trusted for authorisation.
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string EmailVerified = "emailVerified";
    public const string Banned = "banned";
}
