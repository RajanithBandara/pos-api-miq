using POS.Domain.Enums;

namespace POS.Application.Dashboard;

/// <summary>
/// What a verified Neon Auth token actually told us. Every field here has been through
/// signature, issuer, audience and lifetime validation before this record is built — but it is
/// still only a statement about who signed in to Neon Auth, and confers no access to this API.
/// </summary>
public sealed record NeonAuthPrincipal(
    string Subject,
    string? Email,
    bool EmailVerified,
    bool Banned,
    string? Name);

/// <summary>
/// Who this API decided the caller is. Produced only when the Neon subject matches an active
/// grant in our own database, and the role and store scope come from that grant — never from
/// the token.
/// </summary>
public sealed record DashboardIdentity(
    Guid UserId,
    string Subject,
    string Email,
    string? DisplayName,
    DashboardRole Role,
    Guid? StoreScope);

public sealed record DashboardIdentityResponse(
    Guid UserId,
    string Subject,
    string Email,
    string? DisplayName,
    string Role,
    Guid? StoreId,
    string? StoreName,
    bool IsStoreScoped,
    DateTime AuthenticatedAtUtc);

public sealed record DashboardScopeDto(bool AllStores, Guid? StoreId, string Role);

public sealed record DashboardTotalsDto(
    int Stores,
    int Terminals,
    int ActiveTerminals,
    long SyncEvents,
    long SyncEventsLast24Hours,
    DateTime? LastSyncAtUtc);

public sealed record DashboardStoreDto(
    Guid StoreId,
    string Code,
    string Name,
    bool IsActive,
    int Terminals,
    int ActiveTerminals,
    long SyncEvents,
    DateTime? LastSyncAtUtc);

public sealed record DashboardSummaryResponse(
    DashboardScopeDto Scope,
    DashboardTotalsDto Totals,
    IReadOnlyList<DashboardStoreDto> Stores,
    DateTime GeneratedAtUtc);

/// <summary>
/// Pre-authorises an address for the dashboard. There is no subject here on purpose: the person
/// may not have signed in to Neon Auth yet, and their subject cannot be known until they do.
/// </summary>
public sealed record GrantDashboardAccessRequest(
    string Email,
    string? DisplayName,
    DashboardRole Role,
    Guid? StoreId);

public sealed record DashboardUserDto(
    Guid Id,
    string Email,
    string? DisplayName,
    DashboardRole Role,
    Guid? StoreId,
    string? StoreName,
    bool IsActive,
    bool IsLinked,
    DateTime CreatedAtUtc,
    DateTime? LastSeenAtUtc);
