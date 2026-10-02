namespace POS.Api.Authentication;

/// <summary>
/// The two ways a caller can prove who it is.
///
/// Both are bearer tokens, and that is where the similarity ends: a till's token is signed with
/// a secret this API holds, a dashboard user's is signed by Neon Auth with a key only Neon
/// holds. Keeping them as separate schemes rather than one handler with branching logic means
/// the framework decides which validation applies, from the policy on the endpoint — there is no
/// code path where a token validated one way can satisfy the other.
/// </summary>
public static class AuthenticationSchemes
{
    /// <summary>
    /// The existing terminal token: HS256, issued by <c>/api/terminals/token</c>.
    ///
    /// This stays the application's default scheme. Every <c>[Authorize]</c> that does not name a
    /// scheme therefore resolves to it exactly as it did before dashboard authentication existed,
    /// which is what keeps the offline POS untouched by any of this.
    /// </summary>
    public const string Pos = "PosAuth";

    /// <summary>
    /// Neon Auth (Managed Better Auth): EdDSA, issued by Neon, verified against its published
    /// JWKS. Used only by the Angular dashboard.
    /// </summary>
    public const string Neon = "NeonAuth";
}

public static class NeonAuthDefaults
{
    /// <summary>
    /// Named client for fetching Neon's JWKS. Named rather than typed so the handler's
    /// configuration can resolve it without dragging a service dependency into the options.
    /// </summary>
    public const string HttpClientName = "NeonAuthJwks";
}
