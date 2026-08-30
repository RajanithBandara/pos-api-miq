using POS.Domain.Entities;

namespace POS.Application.Common.Interfaces;

public sealed record TerminalToken(string AccessToken, DateTime ExpiresAtUtc, int ExpiresInSeconds);

/// <summary>
/// Mints the short-lived bearer token a till carries on every sync call. The durable secret
/// stays on the device and is exchanged for one of these, so a captured token expires on its
/// own and revoking a terminal takes effect within one token lifetime.
/// </summary>
public interface ITerminalTokenService
{
    TerminalToken Issue(Terminal terminal);
}
