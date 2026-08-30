using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using POS.Domain.Entities;

namespace POS.Infrastructure.Persistence.Configurations;

public sealed class SyncEventConfiguration : IEntityTypeConfiguration<SyncEvent>
{
    public void Configure(EntityTypeBuilder<SyncEvent> builder)
    {
        builder.ToTable("sync_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        // Assigned by the ingest path under this store's write lock, never by the database. See
        // SyncEvent.Sequence for why a database identity is the wrong tool here.
        builder.Property(e => e.Sequence).ValueGeneratedNever();

        builder.Property(e => e.StoreId).IsRequired();
        builder.Property(e => e.TerminalId).IsRequired();
        builder.Property(e => e.EventId).IsRequired();
        builder.Property(e => e.AggregateType).HasMaxLength(64).IsRequired();
        builder.Property(e => e.AggregateId).IsRequired();
        builder.Property(e => e.Operation).HasConversion<int>().IsRequired();

        // jsonb rather than text: the payload is queried in anger later (projections, reports,
        // replaying a single aggregate), and converting a whole table afterwards is painful.
        builder.Property(e => e.Payload).HasColumnType("jsonb").IsRequired();

        builder.Property(e => e.PayloadVersion).IsRequired();
        builder.Property(e => e.OccurredAtUtc).IsRequired();
        builder.Property(e => e.ReceivedAtUtc).IsRequired();

        // The idempotency guarantee itself. Scoped to the store rather than global so two
        // stores cannot collide, and enforced by the database rather than by the check in the
        // service — that check makes duplicates cheap, this makes them impossible.
        builder.HasIndex(e => new { e.StoreId, e.EventId }).IsUnique();

        // The change feed's read path: one store's events after a cursor, in order. Unique
        // because the sequence is gapless within a store, so a duplicate would mean the write
        // lock failed to do its job — worth having the database refuse it rather than serving
        // an ambiguous feed.
        builder.HasIndex(e => new { e.StoreId, e.Sequence }).IsUnique();

        builder.HasIndex(e => new { e.StoreId, e.AggregateType, e.AggregateId });

        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(e => e.StoreId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Terminal>()
            .WithMany()
            .HasForeignKey(e => e.TerminalId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(e => e.DomainEvents);
    }
}
