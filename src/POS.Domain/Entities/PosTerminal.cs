using System;
using System.Collections.Generic;
using POS.Domain.Common;

namespace POS.Domain.Entities;

public class PosTerminal : BaseAuditableEntity<Guid>, ISoftDeletable
{
    public Guid StoreId { get; set; }
    public string TerminalCode { get; set; } = string.Empty;
    public string TerminalName { get; set; } = string.Empty;
    public string? MacAddress { get; set; }
    public string? SerialNumber { get; set; }
    public string? ClientVersion { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public DateTime? LastSyncAtUtc { get; set; }
    public long LastSyncVersion { get; set; }

    // Navigation
    public Store? Store { get; set; }
    public ICollection<Sale> Sales { get; set; } = new List<Sale>();

    public PosTerminal()
    {
        Id = Guid.NewGuid();
    }
}
