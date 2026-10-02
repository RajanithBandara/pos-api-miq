using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace POS.Infrastructure.Authentication;

/// <summary>
/// Turns Neon Auth's JWKS document into the signing keys the bearer handler validates against.
///
/// This is deliberately a <see cref="IConfigurationRetriever{T}"/> rather than a fetch-and-cache
/// service of our own. Plugging it into the handler's <c>ConfigurationManager</c> means key
/// retrieval, caching, periodic refresh and — most importantly — the automatic re-fetch when a
/// token arrives with an unknown <c>kid</c> all come from the framework. Neon rotates its signing
/// keys, and hand-rolled caching is how a rotation turns into an outage.
///
/// Neon publishes JWKS, not OpenID Connect discovery, so the document is read directly and the
/// keys are placed on an otherwise empty configuration. Only signing keys are ever taken from it;
/// nothing in the document can influence issuer or audience validation, which come from
/// configuration this deployment controls.
/// </summary>
public sealed class NeonAuthJwksRetriever(ILogger<NeonAuthJwksRetriever>? logger = null)
    : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    private readonly ILogger _logger = logger ?? NullLogger<NeonAuthJwksRetriever>.Instance;

    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string address,
        IDocumentRetriever retriever,
        CancellationToken cancel)
    {
        var document = await retriever.GetDocumentAsync(address, cancel);
        var keySet = new JsonWebKeySet(document);
        var configuration = new OpenIdConnectConfiguration();

        var accepted = 0;
        foreach (var jwk in keySet.Keys)
        {
            // "enc" keys are for encryption and must never be accepted as signature keys.
            // An absent use is fine — the field is optional and most JWKS omit it.
            if (!string.IsNullOrEmpty(jwk.Use) && jwk.Use != "sig") continue;

            var key = ToSecurityKey(jwk);
            if (key is null) continue;

            configuration.SigningKeys.Add(key);
            accepted++;
        }

        if (accepted == 0)
        {
            // Not thrown: an empty key set leaves the handler with nothing to validate against,
            // so every token is refused. That is the correct outcome, and it is worth a loud log
            // because the symptom — every dashboard user suddenly unauthorised — otherwise looks
            // like an authorisation bug rather than a key-distribution one.
            _logger.LogError(
                "Neon Auth JWKS at {Address} yielded no usable signing keys. Dashboard authentication will refuse every request.",
                address);
        }
        else
        {
            _logger.LogInformation("Loaded {Count} Neon Auth signing key(s) from {Address}.", accepted, address);
        }

        return configuration;
    }

    /// <summary>
    /// Converts one JWK. Ed25519 keys become our own key type because the platform has no
    /// support for them; anything else is handed back as-is, so a future move by Neon to RSA or
    /// EC keys keeps working without a change here.
    /// </summary>
    private SecurityKey? ToSecurityKey(JsonWebKey jwk)
    {
        if (!string.Equals(jwk.Kty, "OKP", StringComparison.Ordinal))
            return jwk;

        if (!string.Equals(jwk.Crv, EdDsaSecurityKey.CurveName, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Ignoring Neon Auth key {KeyId}: curve '{Curve}' is not supported for signature verification.",
                jwk.Kid, jwk.Crv);
            return null;
        }

        try
        {
            return new EdDsaSecurityKey(Base64UrlEncoder.DecodeBytes(jwk.X), jwk.Kid);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            _logger.LogWarning(ex, "Ignoring Neon Auth key {KeyId}: its public key could not be read.", jwk.Kid);
            return null;
        }
    }
}
