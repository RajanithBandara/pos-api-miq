using System;
using POS.Domain.Common;
using POS.Domain.Enums;

namespace POS.Domain.Entities;

public class SyncChangeLog : BaseEntity<long>
{
    public Guid StoreId { get; set; }
    public EntityType EntityType { get; set; }
    public Guid EntityId { get; set; }
    public SyncOperationType OperationType { get; set; } = SyncOperationType.Upsert;
    public long Version { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? SourceTerminalId { get; set; }

    // Navigation
    public Store? Store { get; set; }
    public PosTerminal? SourceTerminal { get; set; }
}

public class SyncIdempotencyRecord : BaseEntity<Guid>
{
    public Guid PosTerminalId { get; set; }
    public Guid StoreId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public int StatusCode { get; set; } = 200;
    public string ResponseJson { get; set; } = string.Empty;
    public DateTime ProcessedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddDays(7);

    // Navigation
    public PosTerminal? PosTerminal { get; set; }
    public Store? Store { get; set; }

    public SyncIdempotencyRecord()
    {
        Id = Guid.NewGuid();
    }
}
