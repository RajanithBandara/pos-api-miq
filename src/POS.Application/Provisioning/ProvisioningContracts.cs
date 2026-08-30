namespace POS.Application.Provisioning;

public sealed record CreateStoreRequest(
    string Code,
    string Name,
    string? Address,
    string? ContactNumber,
    string? TaxRegistrationNumber,
    string? TimeZoneId);

public sealed record StoreDto(
    Guid Id,
    string Code,
    string Name,
    string? Address,
    string? ContactNumber,
    string? TaxRegistrationNumber,
    string TimeZoneId,
    bool IsActive,
    int TerminalCount,
    DateTime CreatedAtUtc);

public sealed record IssueEnrollmentCodeRequest(int? ValidForMinutes, string? Note);

public sealed record EnrollmentCodeDto(
    Guid Id,
    string Code,
    Guid StoreId,
    string StoreName,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    DateTime? UsedAtUtc,
    Guid? UsedByTerminalId,
    bool IsRevoked,
    string? Note);
