using POS.Domain.Enums;

namespace POS.Application.Terminals;

public sealed record EnrollTerminalRequest(
    string EnrollmentCode,
    Guid TerminalUid,
    string? CounterNumber,
    string? CounterName,
    string? MachineName,
    string? AppVersion);

/// <summary>
/// Returned exactly once, at enrollment. <see cref="ApiKey"/> is never retrievable again —
/// only its hash is stored — so a till that loses it needs a fresh enrollment code.
/// </summary>
public sealed record EnrollTerminalResponse(
    Guid TerminalId,
    Guid TerminalUid,
    Guid StoreId,
    string StoreName,
    string StoreCode,
    string ApiKey,
    DateTime EnrolledAtUtc);

public sealed record TerminalTokenRequest(Guid TerminalUid, string ApiKey);

public sealed record TerminalTokenResponse(
    string AccessToken,
    string TokenType,
    DateTime ExpiresAtUtc,
    int ExpiresInSeconds,
    Guid TerminalId,
    Guid StoreId);

public sealed record TerminalDto(
    Guid Id,
    Guid TerminalUid,
    Guid StoreId,
    string StoreName,
    string CounterNumber,
    string CounterName,
    string? MachineName,
    string? AppVersion,
    TerminalStatus Status,
    DateTime EnrolledAtUtc,
    DateTime? LastSeenAtUtc);
