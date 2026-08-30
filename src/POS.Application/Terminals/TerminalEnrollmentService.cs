using Microsoft.Extensions.Logging;
using POS.Application.Common.Interfaces;
using POS.Application.Common.Models;
using POS.Application.Common.Security;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Interfaces;

namespace POS.Application.Terminals;

public interface ITerminalEnrollmentService
{
    Task<Result<EnrollTerminalResponse>> EnrollAsync(EnrollTerminalRequest request, CancellationToken cancellationToken = default);
    Task<Result<TerminalTokenResponse>> IssueTokenAsync(TerminalTokenRequest request, CancellationToken cancellationToken = default);
    Task<Result<TerminalDto>> GetByIdAsync(Guid terminalId, CancellationToken cancellationToken = default);
}

public sealed class TerminalEnrollmentService(
    ITerminalRepository terminals,
    IStoreRepository stores,
    IEnrollmentCodeRepository codes,
    ISecretHasher hasher,
    ITerminalTokenService tokens,
    IUnitOfWork unitOfWork,
    ILogger<TerminalEnrollmentService> logger) : ITerminalEnrollmentService
{
    // Error codes, not prose, so the controller maps outcomes to status codes without
    // matching on message text.
    public const string CodeInvalidEnrollment = "invalid_enrollment_code";
    public const string CodeStoreInactive = "store_inactive";
    public const string CodeTerminalConflict = "terminal_belongs_to_another_store";
    public const string CodeTerminalRevoked = "terminal_revoked";
    public const string CodeInvalidCredentials = "invalid_credentials";
    public const string CodeNotFound = "not_found";

    /// <summary>A real BCrypt hash of a value nothing knows, used only to burn matching time.</summary>
    private const string DummyHash = "$2a$11$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy";

    public async Task<Result<EnrollTerminalResponse>> EnrollAsync(
        EnrollTerminalRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.TerminalUid == Guid.Empty)
            return Result<EnrollTerminalResponse>.Failure("A terminal identifier is required.", CodeInvalidEnrollment);

        if (string.IsNullOrWhiteSpace(request.EnrollmentCode))
            return Result<EnrollTerminalResponse>.Failure("An enrollment code is required.", CodeInvalidEnrollment);

        var normalised = request.EnrollmentCode.Trim().ToUpperInvariant();
        var code = await codes.FindByCodeAsync(normalised, cancellationToken);

        // A missing code and an expired one answer identically. Telling an unauthenticated
        // caller which codes exist would turn this endpoint into an oracle it could sweep;
        // the distinction is kept in the log, where it helps and cannot be probed.
        if (code is null)
        {
            logger.LogWarning("Enrollment refused: code not found (terminal {TerminalUid})", request.TerminalUid);
            return Result<EnrollTerminalResponse>.Failure("That enrollment code is not valid.", CodeInvalidEnrollment);
        }

        var rejection = code.RejectionReason(DateTime.UtcNow);
        if (rejection is not null)
        {
            logger.LogWarning("Enrollment refused for code {CodeId}: {Reason} (terminal {TerminalUid})",
                code.Id, rejection, request.TerminalUid);
            return Result<EnrollTerminalResponse>.Failure("That enrollment code is not valid.", CodeInvalidEnrollment);
        }

        var store = await stores.FindByIdAsync(code.StoreId, cancellationToken);
        if (store is null)
            return Result<EnrollTerminalResponse>.Failure("That enrollment code is not valid.", CodeInvalidEnrollment);

        if (!store.IsActive)
            return Result<EnrollTerminalResponse>.Failure(
                "That store is not active and cannot take new terminals.", CodeStoreInactive);

        var existing = await terminals.FindByUidAsync(request.TerminalUid, cancellationToken);

        // Both refusals are checked before any secret is generated or written, so a rejected
        // enrollment leaves nothing behind.
        if (existing is not null && existing.StoreId != store.Id)
        {
            logger.LogWarning(
                "Enrollment refused: terminal {TerminalUid} belongs to store {CurrentStore}, code targets {TargetStore}",
                request.TerminalUid, existing.StoreId, store.Id);

            return Result<EnrollTerminalResponse>.Failure(
                "This terminal is already enrolled to a different store. Revoke it there before re-enrolling.",
                CodeTerminalConflict);
        }

        if (existing is not null && existing.Status == TerminalStatus.Revoked)
            return Result<EnrollTerminalResponse>.Failure(
                "This terminal has been revoked and cannot be re-enrolled.", CodeTerminalRevoked);

        // Generated outside the transaction on purpose: the operation below may be replayed
        // after a transient database fault, and minting a new secret on each attempt would
        // hand back a key whose hash is not the one that ended up committed.
        var apiKey = SecretGenerator.NewApiKey();
        var apiKeyHash = hasher.Hash(apiKey);

        var terminal = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            Terminal enrolled;
            if (existing is null)
            {
                enrolled = Terminal.Enroll(
                    store.Id,
                    request.TerminalUid,
                    apiKeyHash,
                    request.CounterNumber,
                    request.CounterName,
                    request.MachineName,
                    request.AppVersion);

                await terminals.AddAsync(enrolled, ct);
            }
            else
            {
                // The same physical till enrolling again: a reinstall, or a machine rebuilt
                // from an image. It keeps its identity and everything already filed under it;
                // only the credential changes. That is what makes recovery possible without
                // orphaning the events already attributed to this terminal.
                existing.RotateApiKey(apiKeyHash);
                existing.UpdateDescription(request.CounterNumber, request.CounterName, request.MachineName, request.AppVersion);
                enrolled = existing;
            }

            // Saved before the code is consumed so there is a real terminal id to record
            // against it. Both writes land in the same transaction, so a failure between them
            // cannot leave a spent code with no terminal, or a terminal with a code still
            // spendable by someone else.
            await unitOfWork.SaveChangesAsync(ct);

            code.Consume(enrolled.Id);
            await unitOfWork.SaveChangesAsync(ct);

            return enrolled;
        }, cancellationToken);

        logger.LogInformation("Terminal {TerminalUid} enrolled to store {StoreCode} as {TerminalId}",
            terminal.TerminalUid, store.Code, terminal.Id);

        return Result<EnrollTerminalResponse>.Success(new EnrollTerminalResponse(
            terminal.Id,
            terminal.TerminalUid,
            store.Id,
            store.Name,
            store.Code,
            apiKey,
            terminal.EnrolledAtUtc));
    }

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
