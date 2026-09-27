using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Sql.EntityFrameworkContexts.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessageEntity>
{
    public void Configure(EntityTypeBuilder<OutboxMessageEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("outbox_messages");

        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).HasColumnName("id");

        builder.Property(message => message.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(message => message.ContentType)
            .HasColumnName("content_type")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(message => message.Payload)
            .HasColumnName("payload")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(message => message.OccurredOnUtc).HasColumnName("occurred_on_utc").IsRequired();
        builder.Property(message => message.ProcessedOnUtc).HasColumnName("processed_on_utc");
        builder.Property(message => message.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(message => message.Error).HasColumnName("error").HasColumnType("text");
        builder.Property(message => message.NextAttemptUtc).HasColumnName("next_attempt_utc");
        builder.Property(message => message.TraceParent).HasColumnName("trace_parent").HasMaxLength(128);

        builder
            .HasIndex(message => new { message.ProcessedOnUtc, message.NextAttemptUtc })
            .HasFilter("processed_on_utc IS NULL")
            .HasDatabaseName("ix_outbox_messages_pending");
    }
}
