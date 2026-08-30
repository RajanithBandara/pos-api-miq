using Microsoft.Extensions.Logging;
using POS.Application.Common.Models;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Interfaces;

namespace POS.Application.Sync;

public interface ISyncIngestService
{
    Task<Result<SyncPushResponse>> PushAsync(
        Guid storeId, Guid terminalId, SyncPushRequest request, CancellationToken cancellationToken = default);

    Task<SyncStatusResponse> GetStatusAsync(
        Guid storeId, Guid terminalId, CancellationToken cancellationToken = default);

    Task<Result<SyncPullResponse>> PullAsync(
        Guid storeId, Guid terminalId, long since, int? limit, CancellationToken cancellationToken = default);
}

public sealed class SyncIngestService(
    ISyncEventRepository events,
    ITerminalRepository terminals,
    IUnitOfWork unitOfWork,
    ILogger<SyncIngestService> logger) : ISyncIngestService
{
    /// <summary>
    /// Caps borrowed from what a till realistically produces. A batch beyond this is either a
    /// misconfigured worker or something that is not a till, and either way the right answer is
    /// to refuse it rather than let one request occupy the database for a minute.
    /// </summary>
    public const int MaxEventsPerBatch = 500;
    public const int MaxPayloadBytes = 512 * 1024;
    public const int MaxSupportedPayloadVersion = 1;

    public const int DefaultPullLimit = 200;
    public const int MaxPullLimit = 500;

    public const string CodeEmptyBatch = "empty_batch";
    public const string CodeBatchTooLarge = "batch_too_large";
    public const string CodeInvalidCursor = "invalid_cursor";

    private static readonly HashSet<string> KnownAggregateTypes = new(StringComparer.Ordinal)
    {
        "Order", "CashSession", "ReceiptRefund", "InventoryMovement",
        "ProductBatch", "Customer", "RemovedProductAudit"
    };

    public async Task<Result<SyncPushResponse>> PushAsync(
        Guid storeId,
        Guid terminalId,
        SyncPushRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Events is null || request.Events.Count == 0)
            return Result<SyncPushResponse>.Failure("A push must contain at least one event.", CodeEmptyBatch);

        if (request.Events.Count > MaxEventsPerBatch)
            return Result<SyncPushResponse>.Failure(
                $"A push may contain at most {MaxEventsPerBatch} events; this one had {request.Events.Count}.",
                CodeBatchTooLarge);

        var results = new List<SyncEventResult>(request.Events.Count);
        var toStore = new List<SyncEvent>();

        // Everything this store already holds from the batch, fetched once. A worker that
        // delivered successfully and lost the acknowledgement will resend, so duplicates are
        // the normal case rather than an anomaly, and answering them one query at a time would
        // make the common path the slow one.
        var incomingIds = request.Events.Select(e => e.EventId).Distinct().ToList();
        var known = await events.GetExistingEventIdsAsync(storeId, incomingIds, cancellationToken);

        // Guards against a batch that repeats an id within itself, which the unique index would
        // otherwise catch only at save time, failing the whole batch over one bad row.
        var seenInBatch = new HashSet<Guid>();

        foreach (var incoming in request.Events)
        {
            var rejection = Validate(incoming);
            if (rejection is not null)
            {
                results.Add(new SyncEventResult(incoming.EventId, SyncEventOutcome.Rejected, rejection));
                continue;
            }

            if (known.Contains(incoming.EventId) || !seenInBatch.Add(incoming.EventId))
            {
                results.Add(new SyncEventResult(incoming.EventId, SyncEventOutcome.Duplicate));
                continue;
            }

            toStore.Add(SyncEvent.Receive(
                storeId,
                terminalId,
                incoming.EventId,
                incoming.AggregateType,
                incoming.AggregateId,
                Enum.Parse<SyncOperation>(incoming.Operation, ignoreCase: true),
                incoming.Payload,
                incoming.PayloadVersion,
                DateTime.SpecifyKind(incoming.OccurredAtUtc, DateTimeKind.Utc)));

            results.Add(new SyncEventResult(incoming.EventId, SyncEventOutcome.Accepted));
        }

        if (toStore.Count > 0)
        {
            await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                // Held for the rest of this transaction. Both the read of the current high-water
                // mark and the inserts that follow happen inside it, so sequences are allocated
                // and committed in the same order. See the interface for what goes wrong without
                // it: events that are stored correctly and then never delivered.
                await events.AcquireStoreWriteLockAsync(storeId, ct);

                var sequence = await events.GetHighestSequenceAsync(storeId, ct);
                foreach (var stored in toStore)
                    stored.AssignSequence(++sequence);

                await events.AddRangeAsync(toStore, ct);

                var terminal = await terminals.FindByIdAsync(terminalId, ct);
                terminal?.MarkSeen();

                return await unitOfWork.SaveChangesAsync(ct);
            }, cancellationToken);
        }

        var accepted = results.Count(r => r.Outcome == SyncEventOutcome.Accepted);
        var duplicates = results.Count(r => r.Outcome == SyncEventOutcome.Duplicate);
        var rejected = results.Count(r => r.Outcome == SyncEventOutcome.Rejected);

        if (rejected > 0)
        {
            logger.LogWarning(
                "Push from terminal {TerminalId}: {Accepted} accepted, {Duplicates} duplicate, {Rejected} rejected. First rejection: {Reason}",
                terminalId, accepted, duplicates, rejected,
                results.First(r => r.Outcome == SyncEventOutcome.Rejected).Error);
        }
        else
        {
            logger.LogInformation(
                "Push from terminal {TerminalId}: {Accepted} accepted, {Duplicates} duplicate.",
                terminalId, accepted, duplicates);
        }

        return Result<SyncPushResponse>.Success(
            new SyncPushResponse(accepted, duplicates, rejected, results));
    }

    public async Task<SyncStatusResponse> GetStatusAsync(
        Guid storeId,
        Guid terminalId,
        CancellationToken cancellationToken = default)
    {
        var summary = await events.GetSummaryAsync(storeId, terminalId, cancellationToken);

        return new SyncStatusResponse(
            storeId,
            terminalId,
            summary.StoreCount,
            summary.TerminalCount,
            summary.LastReceivedAtUtc,
            summary.HighestSequence);
    }

    public async Task<Result<SyncPullResponse>> PullAsync(
        Guid storeId,
        Guid terminalId,
        long since,
        int? limit,
        CancellationToken cancellationToken = default)
    {
        if (since < 0)
            return Result<SyncPullResponse>.Failure("The cursor cannot be negative.", CodeInvalidCursor);

        var pageSize = Math.Clamp(limit ?? DefaultPullLimit, 1, MaxPullLimit);

        // The caller's own events are excluded here rather than left for the till to discard.
        // A terminal that applied its own event back would create a second copy of a sale it
        // already holds, and the store's takings would quietly double.
        var page = await events.GetFeedAsync(storeId, since, pageSize, terminalId, cancellationToken);

        var feed = page.Events
            .Select(e => new SyncFeedEvent(
                e.Sequence, e.EventId, e.TerminalId, e.AggregateType, e.AggregateId,
                e.Operation.ToString(), e.Payload, e.PayloadVersion, e.OccurredAtUtc))
            .ToList();

        return Result<SyncPullResponse>.Success(
            new SyncPullResponse(feed, page.NextCursor, page.HasMore));
    }

    /// <summary>
    /// Returns why an event can never be accepted, or null when it can. Everything here is a
    /// permanent fault: the till is told to stop retrying, because a resend would fail
    /// identically and would otherwise block the queue behind it forever.
    /// </summary>
    private static string? Validate(SyncEventPayload incoming)
    {
        if (incoming.EventId == Guid.Empty)
            return "eventId is required.";

        if (incoming.AggregateId == Guid.Empty)
            return "aggregateId is required.";

        if (string.IsNullOrWhiteSpace(incoming.AggregateType) || !KnownAggregateTypes.Contains(incoming.AggregateType))
            return $"'{incoming.AggregateType}' is not an aggregate type this server accepts.";

        if (!Enum.TryParse<SyncOperation>(incoming.Operation, ignoreCase: true, out _))
            return $"'{incoming.Operation}' is not a valid operation.";

        if (string.IsNullOrWhiteSpace(incoming.Payload))
            return "payload is required.";

        if (System.Text.Encoding.UTF8.GetByteCount(incoming.Payload) > MaxPayloadBytes)
            return $"payload exceeds the {MaxPayloadBytes / 1024}KB limit.";

        // A payload written by a newer till than this server understands. Rejecting is honest:
        // storing it would mean holding an event nothing can read, and silently accepting it
        // would let the till believe it had been understood.
        if (incoming.PayloadVersion is < 1 or > MaxSupportedPayloadVersion)
            return $"payloadVersion {incoming.PayloadVersion} is not supported by this server " +
                   $"(supported: 1..{MaxSupportedPayloadVersion}).";

        return null;
    }
}
