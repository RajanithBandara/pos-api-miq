using Microsoft.EntityFrameworkCore;
using POS.Domain.Entities;
using POS.Domain.Interfaces;
using POS.Infrastructure.Persistence;

namespace POS.Infrastructure.Repositories;

public sealed class SyncEventRepository(AppDbContext db) : ISyncEventRepository
{
    public async Task<IReadOnlySet<Guid>> GetExistingEventIdsAsync(
        Guid storeId,
        IReadOnlyCollection<Guid> eventIds,
        CancellationToken cancellationToken = default)
    {
        if (eventIds.Count == 0) return new HashSet<Guid>();

        var found = await db.SyncEvents
            .Where(e => e.StoreId == storeId && eventIds.Contains(e.EventId))
            .Select(e => e.EventId)
            .ToListAsync(cancellationToken);

        return found.ToHashSet();
    }

    public async Task AddRangeAsync(IEnumerable<SyncEvent> events, CancellationToken cancellationToken = default) =>
        await db.SyncEvents.AddRangeAsync(events, cancellationToken);

    public async Task<SyncEventSummary> GetSummaryAsync(
        Guid storeId,
        Guid terminalId,
        CancellationToken cancellationToken = default)
    {
        var forStore = db.SyncEvents.Where(e => e.StoreId == storeId);

        var storeCount = await forStore.LongCountAsync(cancellationToken);
        var terminalCount = await forStore.LongCountAsync(e => e.TerminalId == terminalId, cancellationToken);

        // Both aggregates tolerate an empty table: MaxAsync over no rows throws, so they are
        // projected through a nullable first.
        var lastReceived = await forStore
            .Select(e => (DateTime?)e.ReceivedAtUtc)
            .MaxAsync(cancellationToken);

        var highestSequence = await forStore
            .Select(e => (long?)e.Sequence)
            .MaxAsync(cancellationToken) ?? 0L;

        return new SyncEventSummary(storeCount, terminalCount, lastReceived, highestSequence);
    }

    public async Task AcquireStoreWriteLockAsync(Guid storeId, CancellationToken cancellationToken = default)
    {
        // The in-memory provider used by the tests has no advisory locks, and no concurrency to
        // protect against either — each test gets its own database.
        if (!db.Database.IsNpgsql()) return;

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({LockKeyFor(storeId)})", cancellationToken);
    }

    public async Task<long> GetHighestSequenceAsync(Guid storeId, CancellationToken cancellationToken = default) =>
        await db.SyncEvents
            .Where(e => e.StoreId == storeId)
            .Select(e => (long?)e.Sequence)
            .MaxAsync(cancellationToken) ?? 0L;

    public async Task<SyncFeedPage> GetFeedAsync(
        Guid storeId,
        long afterSequence,
        int limit,
        Guid? excludeTerminalId,
        CancellationToken cancellationToken = default)
    {
        // One extra row is read so "is there more after this page" is answered without a second
        // count query over a table that only ever grows.
        var page = await db.SyncEvents
            .AsNoTracking()
            .Where(e => e.StoreId == storeId && e.Sequence > afterSequence)
            .OrderBy(e => e.Sequence)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = page.Count > limit;
        if (hasMore) page.RemoveAt(page.Count - 1);

        if (page.Count == 0)
            return new SyncFeedPage([], afterSequence, false);

        // Taken before the caller's own events are removed. The cursor tracks how far the reader
        // has looked, not how much it kept.
        var nextCursor = page[^1].Sequence;

        var visible = excludeTerminalId is Guid own
            ? page.Where(e => e.TerminalId != own).ToList()
            : page;

        return new SyncFeedPage(visible, nextCursor, hasMore);
    }

    /// <summary>
    /// Folds a store id into the single bigint an advisory lock takes. Collisions between two
    /// stores would only ever cost a little unnecessary serialisation, never correctness.
    /// </summary>
    private static long LockKeyFor(Guid storeId)
    {
        Span<byte> bytes = stackalloc byte[16];
        storeId.TryWriteBytes(bytes);

        return BitConverter.ToInt64(bytes[..8]) ^ BitConverter.ToInt64(bytes[8..]);
    }
}
