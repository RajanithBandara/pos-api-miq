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
