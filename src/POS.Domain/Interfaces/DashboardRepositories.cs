using POS.Domain.Entities;

namespace POS.Domain.Interfaces;

public interface IDashboardUserRepository
{
    /// <summary>
    /// The grant bound to a Neon Auth subject. This is the lookup every authenticated dashboard
    /// request makes, so it is indexed and answers in one row.
    /// </summary>
    Task<DashboardUser?> FindByAuthSubjectAsync(string authSubject, CancellationToken cancellationToken = default);

    /// <summary>
    /// A grant issued to an address. Used only on first sign-in, to bind a subject to a grant
    /// that was created before the person had an account.
    /// </summary>
    Task<DashboardUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<DashboardUser?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DashboardUser>> GetAllAsync(CancellationToken cancellationToken = default);

    Task AddAsync(DashboardUser user, CancellationToken cancellationToken = default);
}

/// <summary>Totals across whatever store scope the caller is allowed to see.</summary>
public sealed record DashboardTotals(
    int StoreCount,
    int TerminalCount,
    int ActiveTerminalCount,
    long SyncEventCount,
    long SyncEventsLast24Hours,
    DateTime? LastSyncAtUtc);

public sealed record DashboardStoreStatistics(
    Guid StoreId,
    string StoreCode,
    string StoreName,
    bool IsActive,
    int TerminalCount,
    int ActiveTerminalCount,
    long SyncEventCount,
    DateTime? LastSyncAtUtc);

/// <summary>
/// Read-only aggregates for the web dashboard.
///
/// Separate from the write-side repositories because it answers questions, not entities: the
/// dashboard wants counts and last-seen times across a scope, and serving those by loading
/// aggregates and counting them in memory would read the whole event ledger to produce one
/// number.
///
/// Every method takes the caller's store scope rather than a store id from the request. A null
/// scope means "every store" and is only ever passed for a user whose grant says so.
/// </summary>
public interface IDashboardReadRepository
{
    Task<DashboardTotals> GetTotalsAsync(Guid? storeScope, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DashboardStoreStatistics>> GetStoreStatisticsAsync(
        Guid? storeScope, CancellationToken cancellationToken = default);
}
