using POS.Domain.Common;
using POS.Domain.Enums;

namespace POS.Domain.Entities;

/// <summary>
/// One event as it arrived from a till, stored verbatim.
///
/// This is the durable record — the point at which a sale stops existing only on a machine
/// behind a counter. It is deliberately an append-only ledger of what was received rather than
/// a projection of it: the raw payload can be replayed if a projection is ever found to be
/// wrong, and a bug in interpreting an event cannot destroy the event.
/// </summary>
public sealed class SyncEvent : BaseEntity<Guid>
{
    private SyncEvent() { }

    /// <summary>Taken from the caller's token, never from the request body.</summary>
    public Guid StoreId { get; private set; }

    /// <summary>
    /// Which till sent it. Phase 4 uses this to keep a terminal from being handed back its own
    /// events when store-wide history flows down to peers.
    /// </summary>
    public Guid TerminalId { get; private set; }

    /// <summary>
    /// The till's idempotency key. Unique per store, because a worker that delivers an event
    /// and then loses the acknowledgement will send it again, and must not create a second one.
    /// </summary>
    public Guid EventId { get; private set; }

    public string AggregateType { get; private set; } = string.Empty;
    public Guid AggregateId { get; private set; }
    public SyncOperation Operation { get; private set; }

    public string Payload { get; private set; } = string.Empty;
    public int PayloadVersion { get; private set; }

    /// <summary>When it happened at the till, which may be long before it was received.</summary>
    public DateTime OccurredAtUtc { get; private set; }

    public DateTime ReceivedAtUtc { get; private set; }

    /// <summary>
    /// Position in this store's feed: 1, 2, 3 with no gaps. The feed orders on this rather than
    /// on a timestamp, because tills disagree about the time and two events a millisecond apart
    /// on different machines have no reliable order otherwise.
    ///
    /// Assigned explicitly, inside the per-store write lock, rather than by a database identity
    /// column. An identity allocates at insert but only becomes visible at commit, so two
    /// concurrent pushes can commit out of order — a reader would see 101 while 100 was still in
    /// flight, move its cursor past both, and never be offered 100 again. The event would sit in
    /// the table, perfectly stored, and simply never be delivered. Allocating under the same
    /// lock that serialises the insert removes that window entirely, and costs one indexed
    /// lookup per push.
    /// </summary>
    public long Sequence { get; private set; }

    public static SyncEvent Receive(
        Guid storeId,
        Guid terminalId,
        Guid eventId,
        string aggregateType,
        Guid aggregateId,
        SyncOperation operation,
        string payload,
        int payloadVersion,
        DateTime occurredAtUtc)
    {
        return new SyncEvent
        {
            Id = Guid.NewGuid(),
            StoreId = storeId,
            TerminalId = terminalId,
            EventId = eventId,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            Operation = operation,
            Payload = payload,
            PayloadVersion = payloadVersion,
            OccurredAtUtc = occurredAtUtc,
            ReceivedAtUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Called only by the ingest path, while it holds this store's write lock. Assigning outside
    /// that lock would let two pushes hand out the same number.
    /// </summary>
    public void AssignSequence(long sequence)
    {
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "A feed sequence starts at 1.");

        Sequence = sequence;
    }
}
