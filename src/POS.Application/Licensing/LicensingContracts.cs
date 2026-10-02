using System;

namespace POS.Application.Licensing;

public sealed record ValidateProductKeyRequest(string ProductKey);

public sealed record ValidateProductKeyResponse(
    bool Valid,
    string KeyCode,
    string PlanTier,
    int MaxTerminals,
    string Status,
    bool IsStoreAssigned,
    Guid? StoreId = null,
    string? StoreName = null,
    string? StoreCode = null,
    int EnrolledTerminalsCount = 0,
    DateTime? ExpiresAtUtc = null);

public sealed record ActivateStoreLicenseRequest(
    string ProductKey,
    Guid TerminalUid,
    string? StoreName = null,
    string? StoreCode = null,
    string? Address = null,
    string? ContactNumber = null,
    string? TaxRegistrationNumber = null,
    string? CounterNumber = "01",
    string? CounterName = "Main Counter",
    string? MachineName = null,
    string? AppVersion = null);

public sealed record ActivateStoreLicenseResponse(
    Guid TerminalId,
    Guid TerminalUid,
    Guid StoreId,
    string StoreName,
    string StoreCode,
    string ApiKey,
    string PlanTier,
    int MaxTerminals,
    DateTime EnrolledAtUtc);
