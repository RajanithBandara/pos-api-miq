using System;
using System.Collections.Generic;
using System.Linq;
using POS.Domain.Common;
using POS.Domain.Enums;
using POS.Domain.Events;

namespace POS.Domain.Entities;

public class Sale : BaseAuditableEntity<Guid>, IAggregateRoot, ISoftDeletable, IVersionedEntity
{
    public Guid StoreId { get; set; }
    public Guid PosTerminalId { get; set; }
    public Guid? CashierEmployeeId { get; set; }
    public Guid? CustomerId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    
    public decimal SubTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal ChangeAmount { get; set; }
    
    public SaleStatus Status { get; set; } = SaleStatus.Completed;
    public string? Notes { get; set; }
    
    public string? IdempotencyKey { get; set; }
    public Guid? SyncBatchId { get; set; }
    public long RowVersion { get; set; } = 1;
    
    public DateTime CompletedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }

    // Navigation
    public Store? Store { get; set; }
    public PosTerminal? PosTerminal { get; set; }
    public Employee? CashierEmployee { get; set; }
    public Customer? Customer { get; set; }
    
    public ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<StockMovement> StockMovements { get; set; } = new List<StockMovement>();

    public Sale()
    {
        Id = Guid.NewGuid();
    }

    public void CalculateTotals()
    {
        SubTotal = Items.Sum(i => i.Quantity * i.UnitPrice);
        DiscountTotal = Items.Sum(i => i.DiscountAmount);
        TaxTotal = Items.Sum(i => i.TaxAmount);
        GrandTotal = Math.Max(0, SubTotal - DiscountTotal + TaxTotal);
        PaidAmount = Payments.Where(p => p.Status == PaymentStatus.Completed).Sum(p => p.Amount);
        ChangeAmount = Math.Max(0, PaidAmount - GrandTotal);
    }

    public void AddItem(SaleItem item)
    {
        item.SaleId = Id;
        Items.Add(item);
        CalculateTotals();
    }

    public void AddPayment(Payment payment)
    {
        payment.SaleId = Id;
        Payments.Add(payment);
        CalculateTotals();
    }

    public void MarkCompleted()
    {
        Status = SaleStatus.Completed;
        CalculateTotals();
        AddDomainEvent(new SaleCompletedDomainEvent(Id, StoreId, PosTerminalId, InvoiceNumber, GrandTotal, CompletedAtUtc));
    }

    public void VoidSale(string? reason = null)
    {
        Status = SaleStatus.Voided;
        Notes = string.IsNullOrWhiteSpace(Notes) ? $"Voided: {reason}" : $"{Notes} | Voided: {reason}";
    }
}

public class SaleItem : BaseEntity<Guid>
{
    public Guid SaleId { get; set; }
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public Sale? Sale { get; set; }
    public Product? Product { get; set; }

    public SaleItem()
    {
        Id = Guid.NewGuid();
    }

    public void CalculateItemTotal()
    {
        var lineGross = Quantity * UnitPrice;
        var afterDiscount = Math.Max(0, lineGross - DiscountAmount);
        TaxAmount = Math.Round(afterDiscount * TaxRate, 4);
        TotalAmount = afterDiscount + TaxAmount;
    }
}

public class Payment : BaseEntity<Guid>
{
    public Guid SaleId { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string? ReferenceNumber { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Completed;
    public DateTime ProcessedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public Sale? Sale { get; set; }

    public Payment()
    {
        Id = Guid.NewGuid();
    }
}
