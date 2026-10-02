using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using POS.Infrastructure.Authentication;

namespace POS.IntegrationTests.Infrastructure;

/// <summary>
/// Stands in for Neon Auth, issuing real EdDSA-signed tokens from a key pair the test owns.
///
/// It mints tokens rather than stubbing authentication out, which matters: the signature is
/// genuinely Ed25519 and is genuinely verified by the same Bouncy Castle path production uses. A
/// test double that skipped the signature would pass just as happily against an API that had
/// stopped checking it.
///
/// The tokens are written by hand because nothing on this platform can sign EdDSA — which is the
/// whole reason the verification code exists — and because building the payload directly is what
/// lets a test forge a claim and prove the API ignores it.
/// </summary>
public sealed class NeonAuthTestIdentityProvider
{
    /// <summary>Shaped like a real Neon Auth URL: the issuer is its origin, not its full path.</summary>
    public const string AuthUrl = "https://neon-auth.test/neondb/auth";

    public const string Issuer = "https://neon-auth.test";
    public const string Audience = "https://neon-auth.test";
    public const string KeyId = "test-ed25519";

    private readonly Ed25519PrivateKeyParameters _privateKey;
    private readonly Ed25519PublicKeyParameters _publicKey;

    public NeonAuthTestIdentityProvider()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));

        var pair = generator.GenerateKeyPair();
        _privateKey = (Ed25519PrivateKeyParameters)pair.Private;
        _publicKey = (Ed25519PublicKeyParameters)pair.Public;
    }

    /// <summary>The key the API is told to trust, as it would arrive from a JWKS document.</summary>
    public SecurityKey PublicSigningKey => new EdDsaSecurityKey(_publicKey.GetEncoded(), KeyId);

    /// <summary>
    /// Issues a token shaped like one Neon Auth produces. Every parameter that can be wrong in
    /// production can be made wrong here.
    /// </summary>
    public string IssueToken(
        string subject,
        string email,
        bool emailVerified = true,
        bool banned = false,
        string? name = null,
        string? issuer = null,
        string? audience = null,
        TimeSpan? lifetime = null,
        IDictionary<string, object?>? extraClaims = null)
    {
        var issuedAt = DateTimeOffset.UtcNow;
        var expires = issuedAt.Add(lifetime ?? TimeSpan.FromMinutes(15));

        var payload = new Dictionary<string, object?>
        {
            ["iat"] = issuedAt.ToUnixTimeSeconds(),
            ["exp"] = expires.ToUnixTimeSeconds(),
            ["iss"] = issuer ?? Issuer,
            ["aud"] = audience ?? Audience,
            ["sub"] = subject,
            ["id"] = subject,
            ["email"] = email,
            ["emailVerified"] = emailVerified,
            ["name"] = name,
            ["image"] = null,
            // The Postgres RLS role Neon puts in every token. It is not an application role, and
            // the API must never treat it as one.
            ["role"] = "authenticated",
            ["banned"] = banned,
            ["banReason"] = null,
            ["banExpires"] = null
        };

        if (extraClaims is not null)
        {
            foreach (var (key, value) in extraClaims)
                payload[key] = value;
        }

        return Sign(payload, _privateKey);
    }

    /// <summary>
    /// Signs with a key the API has never seen — a correctly-shaped token from the wrong issuer.
    /// </summary>
    public string IssueTokenSignedByAnotherKey(string subject, string email)
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var stranger = (Ed25519PrivateKeyParameters)generator.GenerateKeyPair().Private;

        var now = DateTimeOffset.UtcNow;

        return Sign(new Dictionary<string, object?>
        {
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(15).ToUnixTimeSeconds(),
            ["iss"] = Issuer,
            ["aud"] = Audience,
            ["sub"] = subject,
            ["email"] = email,
            ["emailVerified"] = true,
            ["role"] = "authenticated",
            ["banned"] = false
        }, stranger);
    }

    private static string Sign(Dictionary<string, object?> payload, Ed25519PrivateKeyParameters key)
    {
        var header = Base64UrlEncoder.Encode(
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["alg"] = EdDsaSecurityKey.Algorithm,
                ["typ"] = "JWT",
                ["kid"] = KeyId
            }));

        var body = Base64UrlEncoder.Encode(JsonSerializer.Serialize(payload));
        var signingInput = Encoding.ASCII.GetBytes($"{header}.{body}");

        var signer = new Ed25519Signer();
        signer.Init(forSigning: true, key);
        signer.BlockUpdate(signingInput, 0, signingInput.Length);

        return $"{header}.{body}.{Base64UrlEncoder.Encode(signer.GenerateSignature())}";
    }
}
