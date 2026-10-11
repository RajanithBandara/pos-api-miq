using Microsoft.Extensions.Logging;
using POS.Application.Common.Interfaces;
using POS.Application.Common.Models;
using POS.Application.Common.Security;
using POS.Domain.Entities;
using POS.Domain.Interfaces;

namespace POS.Application.Terminals;

public interface ITerminalEnrollmentService
{
    Task<Result<TerminalTokenResponse>> IssueTokenAsync(TerminalTokenRequest request, CancellationToken cancellationToken = default);
    Task<Result<TerminalDto>> GetByIdAsync(Guid terminalId, CancellationToken cancellationToken = default);
}

public sealed class TerminalEnrollmentService(
    ITerminalRepository terminals,
    ISecretHasher hasher,
    ITerminalTokenService tokens,
    IUnitOfWork unitOfWork,
    ILogger<TerminalEnrollmentService> logger) : ITerminalEnrollmentService
{
    public const string CodeInvalidCredentials = "invalid_credentials";
    public const string CodeNotFound = "not_found";

    /// <summary>A real BCrypt hash of a value nothing knows, used only to burn matching time.</summary>
    private const string DummyHash = "$2a$11$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy";

    public async Task<Result<TerminalTokenResponse>> IssueTokenAsync(
        TerminalTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        var terminal = request.TerminalUid == Guid.Empty
            ? null
            : await terminals.FindByUidAsync(request.TerminalUid, cancellationToken);

        // Every failure here answers identically. An unknown terminal, a suspended one and a
        // wrong key are all "invalid credentials" to the caller, so this endpoint cannot be
        // used to enumerate which terminals exist.
        if (terminal is null || !terminal.CanAuthenticate || string.IsNullOrWhiteSpace(request.ApiKey))
        {
            // Spend the cost of a verify even when there is nothing to verify against, so the
            // response time does not separate "no such terminal" from "wrong key".
            hasher.Verify(request.ApiKey ?? string.Empty, DummyHash);

            logger.LogWarning("Token refused for terminal {TerminalUid}: {Reason}",
                request.TerminalUid,
                terminal is null ? "unknown terminal" : terminal.Status.ToString());

            return Result<TerminalTokenResponse>.Failure(
                "Those terminal credentials are not valid.", CodeInvalidCredentials);
        }

        if (!hasher.Verify(request.ApiKey, terminal.ApiKeyHash))
        {
            logger.LogWarning("Token refused for terminal {TerminalUid}: key mismatch", request.TerminalUid);
            return Result<TerminalTokenResponse>.Failure(
                "Those terminal credentials are not valid.", CodeInvalidCredentials);
        }

        terminal.MarkSeen();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var token = tokens.Issue(terminal);

        return Result<TerminalTokenResponse>.Success(new TerminalTokenResponse(
            token.AccessToken,
            "Bearer",
            token.ExpiresAtUtc,
            token.ExpiresInSeconds,
            terminal.Id,
            terminal.StoreId));
    }

    public async Task<Result<TerminalDto>> GetByIdAsync(Guid terminalId, CancellationToken cancellationToken = default)
    {
        var terminal = await terminals.FindByIdAsync(terminalId, cancellationToken);
        if (terminal is null)
            return Result<TerminalDto>.Failure("Terminal not found.", CodeNotFound);

        return Result<TerminalDto>.Success(new TerminalDto(
            terminal.Id,
            terminal.TerminalUid,
            terminal.StoreId,
            terminal.Store?.Name ?? string.Empty,
            terminal.CounterNumber,
            terminal.CounterName,
            terminal.MachineName,
            terminal.AppVersion,
            terminal.Status,
            terminal.EnrolledAtUtc,
            terminal.LastSeenAtUtc));
    }
}
