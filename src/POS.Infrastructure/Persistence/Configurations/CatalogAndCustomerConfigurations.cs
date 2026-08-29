using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using POS.Domain.Entities;

namespace POS.Infrastructure.Persistence.Configurations;

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("employees");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EmployeeCode).HasMaxLength(50).IsRequired();
        builder.Property(e => e.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.LastName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Email).HasMaxLength(150);
        builder.Property(e => e.Phone).HasMaxLength(30);
        builder.Property(e => e.RoleTitle).HasMaxLength(100);
        builder.Property(e => e.HourlyRate).HasPrecision(18, 4);

        builder.HasOne(e => e.Store)
            .WithMany(s => s.Employees)
            .HasForeignKey(e => e.StoreId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.StoreId, e.EmployeeCode }).IsUnique();
        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(500);

        builder.HasOne(c => c.ParentCategory)
            .WithMany(c => c.SubCategories)
            .HasForeignKey(c => c.ParentCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Sku).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.CostPrice).HasPrecision(18, 4).IsRequired();
        builder.Property(p => p.RetailPrice).HasPrecision(18, 4).IsRequired();
        builder.Property(p => p.TaxRate).HasPrecision(18, 4).HasDefaultValue(0);
        builder.Property(p => p.LowStockThreshold).HasPrecision(18, 4).HasDefaultValue(5);

        builder.HasOne(p => p.Store)
            .WithMany(s => s.Products)
            .HasForeignKey(p => p.StoreId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(p => p.Sku);
        builder.HasIndex(p => new { p.StoreId, p.Sku });
        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}

public class ProductBarcodeConfiguration : IEntityTypeConfiguration<ProductBarcode>
{
    public void Configure(EntityTypeBuilder<ProductBarcode> builder)
    {
        builder.ToTable("product_barcodes");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Barcode).HasMaxLength(100).IsRequired();
        builder.Property(b => b.BarcodeFormat).HasMaxLength(30).HasDefaultValue("EAN13");

        builder.HasOne(b => b.Product)
            .WithMany(p => p.Barcodes)
            .HasForeignKey(b => b.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(b => b.Barcode);
    }
}

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(c => c.LastName).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Email).HasMaxLength(150);
        builder.Property(c => c.Phone).HasMaxLength(30);
        builder.Property(c => c.LoyaltyPoints).HasPrecision(18, 4).HasDefaultValue(0);
        builder.Property(c => c.StoreCreditBalance).HasPrecision(18, 4).HasDefaultValue(0);

        builder.OwnsOne(c => c.Address, a =>
        {
            a.Property(p => p.Street).HasColumnName("address_street").HasMaxLength(200);
            a.Property(p => p.City).HasColumnName("address_city").HasMaxLength(100);
            a.Property(p => p.State).HasColumnName("address_state").HasMaxLength(100);
            a.Property(p => p.PostalCode).HasColumnName("address_postal_code").HasMaxLength(20);
            a.Property(p => p.Country).HasColumnName("address_country").HasMaxLength(100);
        });

        builder.HasOne(c => c.Store)
            .WithMany()
            .HasForeignKey(c => c.StoreId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(c => c.Phone);
        builder.HasIndex(c => c.Email);
        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}
