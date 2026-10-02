using POS.Domain.Interfaces;

namespace POS.Application.Dashboard;

/// <summary>
/// Read-side use cases for the Angular dashboard.
///
/// Every method takes the caller's resolved <see cref="DashboardIdentity"/> rather than a store
/// id from the request. That is what keeps one store's numbers out of another store's dashboard:
/// there is no parameter a caller could supply to widen their own scope, because the scope is not
/// a parameter.
/// </summary>
public interface IDashboardQueryService
{
    /// <summary>
    /// What this API believes about the caller. Answers the dashboard's "who am I, and what am I
    /// allowed to see" question in one call, so the client does not have to infer either.
    /// </summary>
    Task<DashboardIdentityResponse> GetIdentityAsync(
        DashboardIdentity identity, CancellationToken cancellationToken = default);

    Task<DashboardSummaryResponse> GetSummaryAsync(
        DashboardIdentity identity, CancellationToken cancellationToken = default);
}

public sealed class DashboardQueryService(
    IDashboardReadRepository reads,
    IStoreRepository stores) : IDashboardQueryService
{
    public async Task<DashboardIdentityResponse> GetIdentityAsync(
        DashboardIdentity identity,
        CancellationToken cancellationToken = default)
    {
        var store = identity.StoreScope is Guid storeId
            ? await stores.FindByIdAsync(storeId, cancellationToken)
            : null;

        return new DashboardIdentityResponse(
            identity.UserId,
            identity.Subject,
            identity.Email,
            identity.DisplayName,
            identity.Role.ToString(),
            identity.StoreScope,
            store?.Name,
            identity.StoreScope is not null,
            DateTime.UtcNow);
    }

    public async Task<DashboardSummaryResponse> GetSummaryAsync(
        DashboardIdentity identity,
        CancellationToken cancellationToken = default)
    {
        // Null means every store, and it can only be null because the caller's own grant says so.
        var scope = identity.StoreScope;

        var totals = await reads.GetTotalsAsync(scope, cancellationToken);
        var stats = await reads.GetStoreStatisticsAsync(scope, cancellationToken);

        return new DashboardSummaryResponse(
            new DashboardScopeDto(scope is null, scope, identity.Role.ToString()),
            new DashboardTotalsDto(
                totals.StoreCount,
                totals.TerminalCount,
                totals.ActiveTerminalCount,
                totals.SyncEventCount,
                totals.SyncEventsLast24Hours,
                totals.LastSyncAtUtc),
            stats.Select(s => new DashboardStoreDto(
                s.StoreId,
                s.StoreCode,
                s.StoreName,
                s.IsActive,
                s.TerminalCount,
                s.ActiveTerminalCount,
                s.SyncEventCount,
                s.LastSyncAtUtc)).ToList(),
            DateTime.UtcNow);
    }
}
