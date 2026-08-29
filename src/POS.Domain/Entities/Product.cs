using System;
using System.Collections.Generic;
using POS.Domain.Common;

namespace POS.Domain.Entities;

public class Category : BaseAuditableEntity<Guid>, ISoftDeletable, IVersionedEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentCategoryId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public long RowVersion { get; set; } = 1;

    // Navigation
    public Category? ParentCategory { get; set; }
    public ICollection<Category> SubCategories { get; set; } = new List<Category>();
    public ICollection<Product> Products { get; set; } = new List<Product>();

    public Category()
    {
        Id = Guid.NewGuid();
    }
}

public class Product : BaseAuditableEntity<Guid>, ISoftDeletable, IVersionedEntity
{
    public Guid? StoreId { get; set; }
    public Guid? CategoryId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal CostPrice { get; set; }
    public decimal RetailPrice { get; set; }
    public decimal TaxRate { get; set; } // e.g. 0.05 for 5%
    public decimal LowStockThreshold { get; set; } = 5;
    public bool IsActive { get; set; } = true;
    public bool TrackInventory { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public long RowVersion { get; set; } = 1;

    // Navigation
    public Store? Store { get; set; }
    public Category? Category { get; set; }
    public ICollection<ProductBarcode> Barcodes { get; set; } = new List<ProductBarcode>();
    public ICollection<Stock> Stocks { get; set; } = new List<Stock>();
    public ICollection<StockBatch> StockBatches { get; set; } = new List<StockBatch>();
    public ICollection<SaleItem> SaleItems { get; set; } = new List<SaleItem>();

    public Product()
    {
        Id = Guid.NewGuid();
    }
}

public class ProductBarcode : BaseEntity<Guid>
{
    public Guid ProductId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string BarcodeFormat { get; set; } = "EAN13";
    public bool IsPrimary { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public Product? Product { get; set; }

    public ProductBarcode()
    {
        Id = Guid.NewGuid();
    }
}
