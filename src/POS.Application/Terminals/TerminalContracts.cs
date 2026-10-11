using POS.Domain.Enums;

namespace POS.Application.Terminals;

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
