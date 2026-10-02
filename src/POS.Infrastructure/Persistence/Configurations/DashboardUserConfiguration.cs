using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using POS.Domain.Entities;

namespace POS.Infrastructure.Persistence.Configurations;

public sealed class DashboardUserConfiguration : IEntityTypeConfiguration<DashboardUser>
{
    public void Configure(EntityTypeBuilder<DashboardUser> builder)
    {
        builder.ToTable("dashboard_users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        // Long enough for an opaque identifier from an external provider. Neon's is a UUID today,
        // but nothing here depends on that and a wider column costs nothing in Postgres.
        builder.Property(u => u.AuthSubject).HasMaxLength(200);
        builder.Property(u => u.Email).HasMaxLength(256).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(150);
        builder.Property(u => u.Role).HasConversion<int>().IsRequired();
        builder.Property(u => u.IsActive).IsRequired();
        builder.Property(u => u.CreatedAtUtc).IsRequired();
        builder.Property(u => u.CreatedBy).HasMaxLength(100);
        builder.Property(u => u.UpdatedBy).HasMaxLength(100);

        // The lookup every authenticated dashboard request makes. Unique because two grants
        // bound to one account would make "what may this person see" ambiguous, and Postgres
        // permits any number of nulls here, which is what lets grants exist before they are bound.
        builder.HasIndex(u => u.AuthSubject).IsUnique();

        // Grants are addressed to a person, and the address is what an unbound grant is claimed
        // by. Two grants for one address would make that claim ambiguous in exactly the way an
        // access-control decision must not be.
        builder.HasIndex(u => u.Email).IsUnique();

        builder.HasIndex(u => u.StoreId);

        // Restrict, not cascade: deleting a store must not silently widen its users' scope to
        // every store by nulling the column, nor quietly delete the people who could see it.
        builder.HasOne(u => u.Store)
            .WithMany()
            .HasForeignKey(u => u.StoreId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(u => u.DomainEvents);
        builder.Ignore(u => u.CanSignIn);
    }
}
