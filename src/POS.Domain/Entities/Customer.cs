using System;
using System.Collections.Generic;
using POS.Domain.Common;
using POS.Domain.ValueObjects;

namespace POS.Domain.Entities;

public class Customer : BaseAuditableEntity<Guid>, ISoftDeletable, IVersionedEntity
{
    public Guid? StoreId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public Address Address { get; set; } = new();
    public decimal LoyaltyPoints { get; set; }
    public decimal StoreCreditBalance { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public long RowVersion { get; set; } = 1;

    public string FullName => $"{FirstName} {LastName}".Trim();

    // Navigation
    public Store? Store { get; set; }
    public ICollection<Sale> Sales { get; set; } = new List<Sale>();

    public Customer()
    {
        Id = Guid.NewGuid();
    }
}
