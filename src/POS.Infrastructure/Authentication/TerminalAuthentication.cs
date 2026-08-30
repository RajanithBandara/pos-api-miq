using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using POS.Application.Common.Interfaces;
using POS.Domain.Entities;
using POS.Domain.Interfaces;

namespace POS.Infrastructure.Authentication;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string SecretKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "POS-API";
    public string Audience { get; set; } = "POS-Clients";
    public int ExpiryMinutes { get; set; } = 120;
    public int RefreshTokenExpiryDays { get; set; } = 7;
}

/// <summary>
/// Claim names shared by the token issuer and whatever reads the token back. Kept in one
/// place because a typo on either side fails as "unauthenticated" rather than as an error.
/// </summary>
public static class TerminalClaims
{
    public const string TerminalId = "terminal_id";
    public const string TerminalUid = "terminal_uid";
    public const string StoreId = "store_id";
    public const string TokenKind = "token_kind";

    /// <summary>Marks a token as belonging to a till rather than a dashboard user.</summary>
    public const string TerminalTokenKind = "terminal";
}

public sealed class BcryptSecretHasher : ISecretHasher
{
    // Cost 11 is roughly 100ms on current hardware. Enrollment and token exchange are rare
    // enough to afford it; sync calls carry a JWT and never touch this path.
    private const int WorkFactor = 11;

    public string Hash(string secret) => BCrypt.Net.BCrypt.HashPassword(secret, WorkFactor);

    public bool Verify(string secret, string hash)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(hash)) return false;

        try
        {
            return BCrypt.Net.BCrypt.Verify(secret, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // A malformed stored hash must read as "wrong credential", never as an exception
            // that a caller could distinguish from a valid rejection.
            return false;
        }
    }
}

public sealed class JwtTerminalTokenService(IOptions<JwtOptions> options) : ITerminalTokenService
{
    private readonly JwtOptions _options = options.Value;

    public TerminalToken Issue(Terminal terminal)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(_options.ExpiryMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, terminal.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(TerminalClaims.TerminalId, terminal.Id.ToString()),
            new(TerminalClaims.TerminalUid, terminal.TerminalUid.ToString()),
            new(TerminalClaims.StoreId, terminal.StoreId.ToString()),
            new(TerminalClaims.TokenKind, TerminalClaims.TerminalTokenKind),
            new(ClaimTypes.Role, "Terminal")
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt,
            signingCredentials: credentials);

        return new TerminalToken(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt,
            _options.ExpiryMinutes * 60);
    }
}
