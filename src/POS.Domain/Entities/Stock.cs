using System;
using System.Collections.Generic;
using POS.Domain.Common;
using POS.Domain.Enums;
using POS.Domain.Events;
using POS.Domain.Exceptions;

namespace POS.Domain.Entities;

public class Stock : BaseAuditableEntity<Guid>, IAggregateRoot, IVersionedEntity
{
    public Guid StoreId { get; set; }
    public Guid ProductId { get; set; }
    public decimal QuantityOnHand { get; set; }
    public decimal QuantityReserved { get; set; }
    public decimal QuantityAllocated { get; set; }
    public decimal AvailableQuantity => Math.Max(0, QuantityOnHand - QuantityReserved - QuantityAllocated);
    public DateTime? LastCountAtUtc { get; set; }
    public long RowVersion { get; set; } = 1;

    // Navigation
    public Store? Store { get; set; }
    public Product? Product { get; set; }

    public Stock()
    {
        Id = Guid.NewGuid();
    }

    public void IncreaseStock(decimal quantity)
    {
        if (quantity < 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity to increase must be non-negative.");

        QuantityOnHand += quantity;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void DecreaseStock(decimal quantity)
    {
        if (quantity < 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity to decrease must be non-negative.");

        if (QuantityOnHand < quantity)
            throw new InsufficientStockException(ProductId, quantity, QuantityOnHand);

        QuantityOnHand -= quantity;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}

public class StockBatch : BaseAuditableEntity<Guid>, ISoftDeletable, IVersionedEntity
{
    public Guid StoreId { get; set; }
    public Guid ProductId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public decimal CostPrice { get; set; }
    public decimal InitialQuantity { get; set; }
    public decimal CurrentQuantity { get; set; }
    public DateTime? ExpiryDateUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public long RowVersion { get; set; } = 1;

    // Navigation
    public Store? Store { get; set; }
    public Product? Product { get; set; }
    public ICollection<StockMovement> StockMovements { get; set; } = new List<StockMovement>();

    public StockBatch()
    {
        Id = Guid.NewGuid();
    }

    public void DeductQuantity(decimal quantity)
    {
        if (quantity < 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Deduction quantity must be non-negative.");

        if (CurrentQuantity < quantity)
            throw new InsufficientStockException(ProductId, quantity, CurrentQuantity);

        CurrentQuantity -= quantity;
        if (CurrentQuantity == 0)
        {
            IsActive = false;
        }
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void AddQuantity(decimal quantity)
    {
        if (quantity < 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be non-negative.");

        CurrentQuantity += quantity;
        IsActive = true;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}

public class StockMovement : BaseEntity<Guid>, IDomainEvent
{
    public Guid StoreId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? StockBatchId { get; set; }
    public Guid? ReferenceId { get; set; } // e.g. SaleId, PurchaseOrderId, TransferId
    public StockMovementType Type { get; set; } = StockMovementType.Sale;
    public decimal Quantity { get; set; } // positive for incoming, negative for outgoing
    public decimal UnitCost { get; set; }
    public string? Reason { get; set; }
    public Guid? PerformedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    DateTime IDomainEvent.OccurredOnUtc => CreatedAtUtc;

    // Navigation
    public Store? Store { get; set; }
    public Product? Product { get; set; }
    public StockBatch? StockBatch { get; set; }
    public User? PerformedByUser { get; set; }
    public Sale? Sale { get; set; }

    public StockMovement()
    {
        Id = Guid.NewGuid();
    }

    public static StockMovement CreateSaleMovement(
        Guid storeId,
        Guid productId,
        Guid? stockBatchId,
        Guid saleId,
        decimal quantity,
        decimal unitCost,
        Guid? performedByUserId = null)
    {
        return new StockMovement
        {
            StoreId = storeId,
            ProductId = productId,
            StockBatchId = stockBatchId,
            ReferenceId = saleId,
            Type = StockMovementType.Sale,
            Quantity = -Math.Abs(quantity),
            UnitCost = unitCost,
            Reason = $"Sale transaction {saleId}",
            PerformedByUserId = performedByUserId,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}
