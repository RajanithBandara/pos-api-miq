using System;
using System.Collections.Generic;
using POS.Domain.Common;
using POS.Domain.ValueObjects;

namespace POS.Domain.Entities;

public class Store : BaseAuditableEntity<Guid>, ISoftDeletable
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Address Address { get; set; } = new();
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? TaxRegistrationNumber { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }

    // Navigation collections
    public ICollection<PosTerminal> Terminals { get; set; } = new List<PosTerminal>();
    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<Sale> Sales { get; set; } = new List<Sale>();
    public ICollection<Stock> Stocks { get; set; } = new List<Stock>();
    public ICollection<StockBatch> StockBatches { get; set; } = new List<StockBatch>();

    public Store()
    {
        Id = Guid.NewGuid();
    }
}
