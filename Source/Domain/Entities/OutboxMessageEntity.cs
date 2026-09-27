namespace Domain.Entities;

public sealed class OutboxMessageEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string EventType { get; set; }

    public required string ContentType { get; set; }

    public required string Payload { get; set; }

    public DateTime OccurredOnUtc { get; set; }

    public DateTime? ProcessedOnUtc { get; set; }

    public int AttemptCount { get; set; }

    public string? Error { get; set; }

    public DateTime? NextAttemptUtc { get; set; }

    public string? TraceParent { get; set; }
}
