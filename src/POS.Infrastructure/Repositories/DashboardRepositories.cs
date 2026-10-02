using Microsoft.EntityFrameworkCore;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Interfaces;
using POS.Infrastructure.Persistence;

namespace POS.Infrastructure.Repositories;

public sealed class DashboardUserRepository(AppDbContext db) : IDashboardUserRepository
{
    public Task<DashboardUser?> FindByAuthSubjectAsync(string authSubject, CancellationToken cancellationToken = default) =>
        db.DashboardUsers
            .Include(u => u.Store)
            .FirstOrDefaultAsync(u => u.AuthSubject == authSubject, cancellationToken);

    public Task<DashboardUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        // Addresses are stored lower-cased by the entity, so the comparison is done on a
        // normalised value rather than with a case-insensitive collation the index cannot use.
        var normalised = email.Trim().ToLowerInvariant();
        return db.DashboardUsers
            .Include(u => u.Store)
            .FirstOrDefaultAsync(u => u.Email == normalised, cancellationToken);
    }

    public Task<DashboardUser?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.DashboardUsers
            .Include(u => u.Store)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public async Task<IReadOnlyList<DashboardUser>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.DashboardUsers
            .Include(u => u.Store)
            .OrderBy(u => u.Email)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(DashboardUser user, CancellationToken cancellationToken = default) =>
        await db.DashboardUsers.AddAsync(user, cancellationToken);
}

/// <summary>
/// Aggregates for the dashboard's read-only views.
///
/// Everything is counted in the database rather than by loading entities: the event ledger only
/// grows, and materialising it to produce a count would get slower every day the shop trades.
///
/// <c>storeScope</c> is the caller's tenant boundary. Null means every store and is only ever
/// passed for a user whose grant says so — it is never taken from a request.
/// </summary>
public sealed class DashboardReadRepository(AppDbContext db) : IDashboardReadRepository
{
    public async Task<DashboardTotals> GetTotalsAsync(
        Guid? storeScope,
        CancellationToken cancellationToken = default)
    {
        var stores = db.Stores.AsNoTracking()
            .Where(s => storeScope == null || s.Id == storeScope);

        var terminals = db.Terminals.AsNoTracking()
            .Where(t => storeScope == null || t.StoreId == storeScope);

        var events = db.SyncEvents.AsNoTracking()
            .Where(e => storeScope == null || e.StoreId == storeScope);

        var since = DateTime.UtcNow.AddHours(-24);

        return new DashboardTotals(
            await stores.CountAsync(cancellationToken),
            await terminals.CountAsync(cancellationToken),
            await terminals.CountAsync(t => t.Status == TerminalStatus.Active, cancellationToken),
            await events.LongCountAsync(cancellationToken),
            await events.LongCountAsync(e => e.ReceivedAtUtc >= since, cancellationToken),
            // Projected through a nullable so an empty ledger answers null instead of throwing,
            // the same way the sync summary does.
            await events.Select(e => (DateTime?)e.ReceivedAtUtc).MaxAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<DashboardStoreStatistics>> GetStoreStatisticsAsync(
        Guid? storeScope,
        CancellationToken cancellationToken = default)
    {
        var stores = await db.Stores.AsNoTracking()
            .Where(s => storeScope == null || s.Id == storeScope)
            .OrderBy(s => s.Code)
            .ToListAsync(cancellationToken);

        if (stores.Count == 0) return [];

        var storeIds = stores.Select(s => s.Id).ToList();

        // Two grouped queries and an in-memory stitch, rather than one query joining both.
        // Joining terminals and events together would multiply one set of rows by the other
        // before grouping, and every count would come back inflated — a bug that reads as a data
        // problem rather than a query one. A chain has few enough stores that the stitch is free.
        var terminalCounts = await db.Terminals.AsNoTracking()
            .Where(t => storeIds.Contains(t.StoreId))
            .GroupBy(t => t.StoreId)
            .Select(g => new
            {
                StoreId = g.Key,
                Total = g.Count(),
                // A conditional sum rather than a filtered count: it translates the same way on
                // every provider, including the in-memory one the tests run against.
                Active = g.Sum(t => t.Status == TerminalStatus.Active ? 1 : 0)
            })
            .ToDictionaryAsync(x => x.StoreId, cancellationToken);

        var eventCounts = await db.SyncEvents.AsNoTracking()
            .Where(e => storeIds.Contains(e.StoreId))
            .GroupBy(e => e.StoreId)
            .Select(g => new
            {
                StoreId = g.Key,
                Count = g.LongCount(),
                Last = (DateTime?)g.Max(e => e.ReceivedAtUtc)
            })
            .ToDictionaryAsync(x => x.StoreId, cancellationToken);

        return stores.Select(store =>
        {
            terminalCounts.TryGetValue(store.Id, out var terminals);
            eventCounts.TryGetValue(store.Id, out var events);

            return new DashboardStoreStatistics(
                store.Id,
                store.Code,
                store.Name,
                store.IsActive,
                terminals?.Total ?? 0,
                terminals?.Active ?? 0,
                events?.Count ?? 0,
                events?.Last);
        }).ToList();
    }
}
