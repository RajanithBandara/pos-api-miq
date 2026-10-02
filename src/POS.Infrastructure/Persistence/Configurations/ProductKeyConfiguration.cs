using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using POS.Domain.Entities;

namespace POS.Infrastructure.Persistence.Configurations;

public sealed class ProductKeyConfiguration : IEntityTypeConfiguration<ProductKey>
{
    public void Configure(EntityTypeBuilder<ProductKey> builder)
    {
        builder.ToTable("product_keys");

        builder.HasKey(k => k.Id);

        builder.Property(k => k.KeyCode)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(k => k.KeyCode)
            .IsUnique();

        builder.Property(k => k.PlanTier)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(k => k.MaxTerminals)
            .IsRequired()
            .HasDefaultValue(3);

        builder.Property(k => k.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.HasOne(k => k.AssignedStore)
            .WithMany()
            .HasForeignKey(k => k.AssignedStoreId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(k => k.CreatedAtUtc).IsRequired();
        builder.Property(k => k.UpdatedAtUtc);
        builder.Property(k => k.ActivatedAtUtc);
        builder.Property(k => k.ExpiresAtUtc);

        // Seed default test key: MIQ-PRO-7K4D-92LX-AB83
        builder.HasData(new
        {
            Id = Guid.Parse("a0000000-0000-0000-0000-000000000001"),
            KeyCode = "MIQ-PRO-7K4D-92LX-AB83",
            PlanTier = "PRO",
            MaxTerminals = 3,
            Status = ProductKeyStatus.Available,
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
    }
}
