namespace POS.Application.Sync;

/// <summary>
/// One event as the till sends it. Note there is no store or terminal on this record: both are
/// read from the caller's token, so a terminal cannot file events against a store it does not
/// belong to no matter what it puts in the body.
/// </summary>
public sealed record SyncEventPayload(
    Guid EventId,
    string AggregateType,
    Guid AggregateId,
    string Operation,
    string Payload,
    int PayloadVersion,
    DateTime OccurredAtUtc);

public sealed record SyncPushRequest(IReadOnlyList<SyncEventPayload> Events);

public enum SyncEventOutcome
{
    /// <summary>Stored for the first time.</summary>
    Accepted = 0,

    /// <summary>Already held. The till may mark it sent; re-delivery is expected, not an error.</summary>
    Duplicate = 1,

    /// <summary>
    /// Permanently unacceptable — malformed, unknown type, oversized. The till must stop
    /// retrying it, because sending it again will fail in exactly the same way.
    /// </summary>
    Rejected = 2
}

public sealed record SyncEventResult(Guid EventId, SyncEventOutcome Outcome, string? Error = null);

public sealed record SyncPushResponse(
    int Accepted,
    int Duplicates,
    int Rejected,
    IReadOnlyList<SyncEventResult> Results);

public sealed record SyncStatusResponse(
    Guid StoreId,
    Guid TerminalId,
    long EventsHeldForStore,
    long EventsHeldForTerminal,
    DateTime? LastReceivedAtUtc,
    long HighestSequence);

/// <summary>
/// One event on its way back down to a till. Carries the sequence so a client can reason about
/// ordering, and the origin terminal so a whole-store view can say where a sale was rung.
/// </summary>
public sealed record SyncFeedEvent(
    long Sequence,
    Guid EventId,
    Guid TerminalId,
    string AggregateType,
    Guid AggregateId,
    string Operation,
    string Payload,
    int PayloadVersion,
    DateTime OccurredAtUtc);

public sealed record SyncPullResponse(
    IReadOnlyList<SyncFeedEvent> Events,
    long NextCursor,
    bool HasMore);
