using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using POS.Domain.Entities;

namespace POS.Infrastructure.Persistence.Configurations;

public sealed class StoreConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> builder)
    {
        builder.ToTable("stores");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.Code).HasMaxLength(32).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(150).IsRequired();
        builder.Property(s => s.Address).HasMaxLength(300);
        builder.Property(s => s.ContactNumber).HasMaxLength(64);
        builder.Property(s => s.TaxRegistrationNumber).HasMaxLength(64);
        builder.Property(s => s.TimeZoneId).HasMaxLength(64).IsRequired();
        builder.Property(s => s.IsActive).IsRequired();
        builder.Property(s => s.CreatedAtUtc).IsRequired();
        builder.Property(s => s.CreatedBy).HasMaxLength(100);
        builder.Property(s => s.UpdatedBy).HasMaxLength(100);

        builder.HasIndex(s => s.Code).IsUnique();

        builder.HasMany(s => s.Terminals)
            .WithOne(t => t.Store)
            .HasForeignKey(t => t.StoreId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(s => s.DomainEvents);
    }
}

public sealed class TerminalConfiguration : IEntityTypeConfiguration<Terminal>
{
    public void Configure(EntityTypeBuilder<Terminal> builder)
    {
        builder.ToTable("terminals");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.TerminalUid).IsRequired();
        builder.Property(t => t.CounterNumber).HasMaxLength(16).IsRequired();
        builder.Property(t => t.CounterName).HasMaxLength(100).IsRequired();
        builder.Property(t => t.MachineName).HasMaxLength(100);
        builder.Property(t => t.AppVersion).HasMaxLength(32);
        builder.Property(t => t.Status).HasConversion<int>().IsRequired();
        builder.Property(t => t.ApiKeyHash).HasMaxLength(100).IsRequired();
        builder.Property(t => t.EnrolledAtUtc).IsRequired();
        builder.Property(t => t.CreatedAtUtc).IsRequired();
        builder.Property(t => t.CreatedBy).HasMaxLength(100);
        builder.Property(t => t.UpdatedBy).HasMaxLength(100);

        // Unique across the whole API, not per store. A till that turns up claiming a
        // different store is a mistake worth catching rather than a second row, and the
        // token endpoint looks terminals up by this alone.
        builder.HasIndex(t => t.TerminalUid).IsUnique();
        builder.HasIndex(t => t.StoreId);

        builder.Ignore(t => t.DomainEvents);
        builder.Ignore(t => t.CanAuthenticate);
    }
}

public sealed class TerminalEnrollmentCodeConfiguration : IEntityTypeConfiguration<TerminalEnrollmentCode>
{
    public void Configure(EntityTypeBuilder<TerminalEnrollmentCode> builder)
    {
        builder.ToTable("terminal_enrollment_codes");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Code).HasMaxLength(16).IsRequired();
        builder.Property(c => c.CreatedAtUtc).IsRequired();
        builder.Property(c => c.ExpiresAtUtc).IsRequired();
        builder.Property(c => c.IsRevoked).IsRequired();
        builder.Property(c => c.Note).HasMaxLength(200);

        // The enrollment endpoint is unauthenticated and looks codes up by this value, so it
        // has to be both unique and indexed for a single-row hit.
        builder.HasIndex(c => c.Code).IsUnique();
        builder.HasIndex(c => c.StoreId);

        builder.HasOne(c => c.Store)
            .WithMany()
            .HasForeignKey(c => c.StoreId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(c => c.DomainEvents);
    }
}
