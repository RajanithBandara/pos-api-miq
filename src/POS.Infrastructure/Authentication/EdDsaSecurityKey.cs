using Microsoft.IdentityModel.Tokens;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace POS.Infrastructure.Authentication;

/// <summary>
/// Ed25519 signature verification for Microsoft.IdentityModel.
///
/// Neon Auth signs its access tokens with EdDSA. .NET has no Ed25519 implementation — the API
/// proposal for one is still open and unscheduled — and Microsoft.IdentityModel has no EdDSA
/// signature provider, so a Neon token presented to a stock <c>JwtBearer</c> handler fails with
/// "algorithm not supported" no matter how the options are configured.
///
/// The three types here fill exactly that gap and nothing else: a key that holds an Ed25519
/// public key, a provider that verifies against it, and a factory that offers the provider when
/// the algorithm matches. Everything else about validation — issuer, audience, lifetime,
/// algorithm restriction, key rotation — stays with the framework, which is the point. Custom
/// cryptography is confined to the one operation the platform cannot do.
/// </summary>
public sealed class EdDsaSecurityKey : AsymmetricSecurityKey
{
    /// <summary>The JOSE algorithm identifier Neon's tokens carry in their header.</summary>
    public const string Algorithm = "EdDSA";

    /// <summary>The JWK curve name for Ed25519 keys, which arrive as <c>kty: "OKP"</c>.</summary>
    public const string CurveName = "Ed25519";

    private const int PublicKeyLength = 32;

    public EdDsaSecurityKey(byte[] publicKey, string? keyId = null)
    {
        ArgumentNullException.ThrowIfNull(publicKey);

        if (publicKey.Length != PublicKeyLength)
            throw new ArgumentException(
                $"An Ed25519 public key is {PublicKeyLength} bytes; this one was {publicKey.Length}.",
                nameof(publicKey));

        PublicKey = new Ed25519PublicKeyParameters(publicKey, 0);
        KeyId = keyId;

        // Signature providers are created through the key's own factory, so pointing it at ours
        // is what makes the handler willing to verify EdDSA at all.
        CryptoProviderFactory = new EdDsaCryptoProviderFactory();
    }

    public Ed25519PublicKeyParameters PublicKey { get; }

    public override int KeySize => 256;

    // Verification only. This API never issues Neon Auth tokens — Neon does — so there is no
    // private key here and no code path that would want one.
    public override PrivateKeyStatus PrivateKeyStatus => PrivateKeyStatus.DoesNotExist;

#pragma warning disable CS0672 // Deprecated in favour of PrivateKeyStatus, still abstract on the base.
    public override bool HasPrivateKey => false;
#pragma warning restore CS0672
}

/// <summary>
/// Offers <see cref="EdDsaSignatureProvider"/> for EdDSA keys and defers everything else to the
/// stock factory, so a deployment whose JWKS also carries RSA or EC keys keeps working.
/// </summary>
public sealed class EdDsaCryptoProviderFactory : CryptoProviderFactory
{
    public override bool IsSupportedAlgorithm(string algorithm, SecurityKey key) =>
        IsEdDsa(algorithm, key) || base.IsSupportedAlgorithm(algorithm, key);

    public override SignatureProvider CreateForVerifying(SecurityKey key, string algorithm) =>
        IsEdDsa(algorithm, key)
            ? new EdDsaSignatureProvider((EdDsaSecurityKey)key, algorithm)
            : base.CreateForVerifying(key, algorithm);

    public override SignatureProvider CreateForVerifying(SecurityKey key, string algorithm, bool cacheProvider) =>
        IsEdDsa(algorithm, key)
            ? new EdDsaSignatureProvider((EdDsaSecurityKey)key, algorithm)
            : base.CreateForVerifying(key, algorithm, cacheProvider);

    public override void ReleaseSignatureProvider(SignatureProvider signatureProvider)
    {
        if (signatureProvider is EdDsaSignatureProvider)
        {
            signatureProvider.Dispose();
            return;
        }

        base.ReleaseSignatureProvider(signatureProvider);
    }

    private static bool IsEdDsa(string algorithm, SecurityKey key) =>
        key is EdDsaSecurityKey && string.Equals(algorithm, EdDsaSecurityKey.Algorithm, StringComparison.Ordinal);
}

/// <summary>
/// Verifies an Ed25519 signature over the token's signing input using Bouncy Castle.
/// </summary>
public sealed class EdDsaSignatureProvider : SignatureProvider
{
    private readonly Ed25519PublicKeyParameters _publicKey;

    public EdDsaSignatureProvider(EdDsaSecurityKey key, string algorithm) : base(key, algorithm)
    {
        _publicKey = key.PublicKey;
    }

    public override bool Verify(byte[] input, byte[] signature)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(signature);

        return Verify(input.AsSpan(), signature.AsSpan());
    }

    public override bool Verify(
        byte[] input, int inputOffset, int inputLength,
        byte[] signature, int signatureOffset, int signatureLength)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(signature);

        return Verify(
            input.AsSpan(inputOffset, inputLength),
            signature.AsSpan(signatureOffset, signatureLength));
    }

    private bool Verify(ReadOnlySpan<byte> input, ReadOnlySpan<byte> signature)
    {
        // A fresh signer per call: Ed25519Signer accumulates the message internally, and
        // providers are cached and shared across concurrent requests. Reusing one would let two
        // requests interleave their bytes into a single buffer, which fails closed but
        // unpredictably — the worst kind of authentication bug to diagnose.
        var verifier = new Ed25519Signer();
        verifier.Init(forSigning: false, _publicKey);
        verifier.BlockUpdate(input);

        return verifier.VerifySignature(signature.ToArray());
    }

    /// <summary>
    /// Always throws. This API verifies Neon Auth tokens; it never mints them, and a call here
    /// would mean something is trying to impersonate the identity provider.
    /// </summary>
    public override byte[] Sign(byte[] input) =>
        throw new NotSupportedException("This API verifies Neon Auth tokens; it does not issue them.");

    protected override void Dispose(bool disposing)
    {
        // Nothing to release: the public key parameters are immutable and hold no native handle.
    }
}
