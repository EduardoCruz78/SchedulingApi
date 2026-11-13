using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scheduling.Infrastructure.Outbox;

namespace Scheduling.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).HasMaxLength(100);
        builder.Property(m => m.Payload).HasColumnType("jsonb");
        builder.Property(m => m.LastError).HasMaxLength(1000);
        builder.Ignore(m => m.IsPending);

        // The dispatcher only ever looks for pending messages, oldest first: a partial index keeps
        // that lookup small even when millions of processed rows accumulate.
        builder.HasIndex(m => m.OccurredAt)
            .HasDatabaseName("ix_outbox_messages_pending")
            .HasFilter("processed_at IS NULL");
    }
}
