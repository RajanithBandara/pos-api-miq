using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using POS.Domain.Entities;

namespace POS.Infrastructure.Persistence.Configurations;

public class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.ToTable("sales");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.InvoiceNumber).HasMaxLength(100).IsRequired();
        builder.Property(s => s.SubTotal).HasPrecision(18, 4).IsRequired();
        builder.Property(s => s.TaxTotal).HasPrecision(18, 4).IsRequired();
        builder.Property(s => s.DiscountTotal).HasPrecision(18, 4).IsRequired();
        builder.Property(s => s.GrandTotal).HasPrecision(18, 4).IsRequired();
        builder.Property(s => s.PaidAmount).HasPrecision(18, 4).IsRequired();
        builder.Property(s => s.ChangeAmount).HasPrecision(18, 4).IsRequired();
        builder.Property(s => s.Notes).HasMaxLength(1000);
        builder.Property(s => s.IdempotencyKey).HasMaxLength(100);

        builder.HasOne(s => s.Store)
            .WithMany(st => st.Sales)
            .HasForeignKey(s => s.StoreId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.PosTerminal)
            .WithMany(t => t.Sales)
            .HasForeignKey(s => s.PosTerminalId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.CashierEmployee)
            .WithMany(e => e.SalesProcessed)
            .HasForeignKey(s => s.CashierEmployeeId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(s => s.Customer)
            .WithMany(c => c.Sales)
            .HasForeignKey(s => s.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(s => s.Items)
            .WithOne(i => i.Sale)
            .HasForeignKey(i => i.SaleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(s => s.Payments)
            .WithOne(p => p.Sale)
            .HasForeignKey(p => p.SaleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => s.InvoiceNumber);
        builder.HasIndex(s => new { s.StoreId, s.InvoiceNumber });
        builder.HasIndex(s => new { s.StoreId, s.IdempotencyKey }).IsUnique().HasFilter("idempotency_key IS NOT NULL");
        builder.HasIndex(s => s.CompletedAtUtc);
        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}

public class SaleItemConfiguration : IEntityTypeConfiguration<SaleItem>
{
    public void Configure(EntityTypeBuilder<SaleItem> builder)
    {
        builder.ToTable("sale_items");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Sku).HasMaxLength(50).IsRequired();
        builder.Property(i => i.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(i => i.Quantity).HasPrecision(18, 4).IsRequired();
        builder.Property(i => i.UnitCost).HasPrecision(18, 4).IsRequired();
        builder.Property(i => i.UnitPrice).HasPrecision(18, 4).IsRequired();
        builder.Property(i => i.DiscountAmount).HasPrecision(18, 4).HasDefaultValue(0);
        builder.Property(i => i.TaxRate).HasPrecision(18, 4).HasDefaultValue(0);
        builder.Property(i => i.TaxAmount).HasPrecision(18, 4).HasDefaultValue(0);
        builder.Property(i => i.TotalAmount).HasPrecision(18, 4).IsRequired();

        builder.HasOne(i => i.Product)
            .WithMany(p => p.SaleItems)
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Amount).HasPrecision(18, 4).IsRequired();
        builder.Property(p => p.Currency).HasMaxLength(3).HasDefaultValue("USD").IsRequired();
        builder.Property(p => p.ReferenceNumber).HasMaxLength(100);

        builder.HasIndex(p => p.ReferenceNumber);
    }
}

public class StockConfiguration : IEntityTypeConfiguration<Stock>
{
    public void Configure(EntityTypeBuilder<Stock> builder)
    {
        builder.ToTable("stocks");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.QuantityOnHand).HasPrecision(18, 4).HasDefaultValue(0);
        builder.Property(s => s.QuantityReserved).HasPrecision(18, 4).HasDefaultValue(0);
        builder.Property(s => s.QuantityAllocated).HasPrecision(18, 4).HasDefaultValue(0);

        builder.HasOne(s => s.Store)
            .WithMany(st => st.Stocks)
            .HasForeignKey(s => s.StoreId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Product)
            .WithMany(p => p.Stocks)
            .HasForeignKey(s => s.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.StoreId, s.ProductId }).IsUnique();
    }
}

public class StockBatchConfiguration : IEntityTypeConfiguration<StockBatch>
{
    public void Configure(EntityTypeBuilder<StockBatch> builder)
    {
        builder.ToTable("stock_batches");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.BatchNumber).HasMaxLength(50).IsRequired();
        builder.Property(b => b.CostPrice).HasPrecision(18, 4).IsRequired();
        builder.Property(b => b.InitialQuantity).HasPrecision(18, 4).IsRequired();
        builder.Property(b => b.CurrentQuantity).HasPrecision(18, 4).IsRequired();

        builder.HasOne(b => b.Store)
            .WithMany(st => st.StockBatches)
            .HasForeignKey(b => b.StoreId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(b => b.Product)
            .WithMany(p => p.StockBatches)
            .HasForeignKey(b => b.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(b => new { b.StoreId, b.ProductId, b.BatchNumber }).IsUnique();
        builder.HasQueryFilter(b => !b.IsDeleted);
    }
}

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Quantity).HasPrecision(18, 4).IsRequired();
        builder.Property(m => m.UnitCost).HasPrecision(18, 4).IsRequired();
        builder.Property(m => m.Reason).HasMaxLength(500);

        builder.HasOne(m => m.Store)
            .WithMany()
            .HasForeignKey(m => m.StoreId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Product)
            .WithMany()
            .HasForeignKey(m => m.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.StockBatch)
            .WithMany(b => b.StockMovements)
            .HasForeignKey(m => m.StockBatchId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(m => m.PerformedByUser)
            .WithMany()
            .HasForeignKey(m => m.PerformedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(m => m.Sale)
            .WithMany(s => s.StockMovements)
            .HasForeignKey(m => m.ReferenceId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(m => new { m.StoreId, m.ProductId });
        builder.HasIndex(m => m.CreatedAtUtc);
    }
}

public class SyncChangeLogConfiguration : IEntityTypeConfiguration<SyncChangeLog>
{
    public void Configure(EntityTypeBuilder<SyncChangeLog> builder)
    {
        builder.ToTable("sync_change_logs");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.PayloadJson).IsRequired();

        builder.HasOne(c => c.Store)
            .WithMany()
            .HasForeignKey(c => c.StoreId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.SourceTerminal)
            .WithMany()
            .HasForeignKey(c => c.SourceTerminalId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(c => new { c.StoreId, c.Version });
        builder.HasIndex(c => c.CreatedAtUtc);
    }
}

public class SyncIdempotencyRecordConfiguration : IEntityTypeConfiguration<SyncIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<SyncIdempotencyRecord> builder)
    {
        builder.ToTable("sync_idempotency_records");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.IdempotencyKey).HasMaxLength(100).IsRequired();
        builder.Property(r => r.RequestHash).HasMaxLength(100).IsRequired();
        builder.Property(r => r.ResponseJson).IsRequired();

        builder.HasOne(r => r.PosTerminal)
            .WithMany()
            .HasForeignKey(r => r.PosTerminalId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.Store)
            .WithMany()
            .HasForeignKey(r => r.StoreId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.PosTerminalId, r.IdempotencyKey }).IsUnique();
        builder.HasIndex(r => r.ExpiresAtUtc);
    }
}
