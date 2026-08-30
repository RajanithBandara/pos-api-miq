using POS.Domain.Entities;

namespace POS.Domain.Interfaces;

public interface IStoreRepository
{
    Task<Store?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Store?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Store>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task AddAsync(Store store, CancellationToken cancellationToken = default);
}

public interface ITerminalRepository
{
    Task<Terminal?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Looks a till up by the identity it minted for itself.</summary>
    Task<Terminal?> FindByUidAsync(Guid terminalUid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Terminal>> GetByStoreAsync(Guid storeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Terminal>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Terminal terminal, CancellationToken cancellationToken = default);
}

public interface IEnrollmentCodeRepository
{
    Task<TerminalEnrollmentCode?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TerminalEnrollmentCode>> GetByStoreAsync(Guid storeId, CancellationToken cancellationToken = default);
    Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task AddAsync(TerminalEnrollmentCode code, CancellationToken cancellationToken = default);
}

public sealed record SyncEventSummary(
    long StoreCount,
    long TerminalCount,
    DateTime? LastReceivedAtUtc,
    long HighestSequence);

public sealed record SyncFeedPage(
    IReadOnlyList<SyncEvent> Events,
    long NextCursor,
    bool HasMore);

public interface ISyncEventRepository
{
    /// <summary>
    /// Which of <paramref name="eventIds"/> this store already holds. Answered in one query
    /// because re-delivery is the normal case, not an exception.
    /// </summary>
    Task<IReadOnlySet<Guid>> GetExistingEventIdsAsync(
        Guid storeId, IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IEnumerable<SyncEvent> events, CancellationToken cancellationToken = default);

    Task<SyncEventSummary> GetSummaryAsync(
        Guid storeId, Guid terminalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Serialises event insertion for one store, for the duration of the calling transaction.
    ///
    /// This is what makes the change feed safe to read with a simple cursor. Sequence numbers
    /// are read and assigned inside this lock, so they are allocated and committed in the same
    /// order. Without it, two concurrent pushes could either take the same number or commit out
    /// of order — and a reader that saw 101 while 100 was still in flight would move its cursor
    /// past both and never be offered 100 again. The event would sit in the table, perfectly
    /// stored, and simply never be delivered.
    ///
    /// The cost is that pushes for a single store queue behind each other, which for a handful
    /// of tills sending small batches every thirty seconds is not a cost at all.
    /// </summary>
    Task AcquireStoreWriteLockAsync(Guid storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Highest sequence this store has used, or 0 when it has none. Only meaningful while the
    /// store write lock is held &mdash; without it, two pushes read the same value and collide.
    /// </summary>
    Task<long> GetHighestSequenceAsync(Guid storeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of a store's events after <paramref name="afterSequence"/>, oldest first.
    ///
    /// <paramref name="excludeTerminalId"/> drops the caller's own events from the page but
    /// deliberately does not affect the cursor: it still advances past them. Filtering the
    /// cursor as well would leave a busy till re-scanning its own history forever.
    /// </summary>
    Task<SyncFeedPage> GetFeedAsync(
        Guid storeId,
        long afterSequence,
        int limit,
        Guid? excludeTerminalId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One commit boundary for a use case. Enrollment in particular writes a terminal and
/// consumes a code, and a crash between those two would either hand out a credential with
/// the code still spendable, or burn the code with no terminal to show for it.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside a transaction, committing if it returns and
    /// rolling back if it throws.
    ///
    /// Deliberately a callback rather than a begin/commit pair the caller drives. Connections
    /// to a serverless database are retried on transient faults, and a retry has to replay
    /// the whole transaction as one unit — an externally-held transaction cannot be replayed,
    /// so that combination throws at runtime rather than compiling into something workable.
    /// Passing the work in is what lets both concerns coexist. The operation must therefore
    /// be safe to run more than once.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One-way hashing for device secrets. Named for what it protects rather than the algorithm,
/// so the algorithm can be changed without the call sites lying about what they do.
/// </summary>
public interface ISecretHasher
{
    string Hash(string secret);
    bool Verify(string secret, string hash);
}
