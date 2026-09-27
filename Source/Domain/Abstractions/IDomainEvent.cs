namespace Domain.Abstractions;

public interface IDomainEvent
{
    Guid EventId { get; }

    DateTime OccurredOnUtc { get; }
}

public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();

    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}
