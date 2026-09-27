using Domain.Interfaces.Integration;

namespace Domain.Events.Integration;

public sealed record ProductCreatedIntegrationEvent : IIntegrationEvent
{
    public const string Name = "product.created";

    public Guid EventId { get; init; } = Guid.CreateVersion7();

    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;

    public string EventType => Name;

    public required Guid ProductId { get; init; }

    public required string Sku { get; init; }

    public required string ProductName { get; init; }
}

public sealed record ProductPriceChangedIntegrationEvent : IIntegrationEvent
{
    public const string Name = "product.price-changed";

    public Guid EventId { get; init; } = Guid.CreateVersion7();

    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;

    public string EventType => Name;

    public required Guid ProductId { get; init; }

    public required string Sku { get; init; }

    public required decimal PreviousAmount { get; init; }

    public required decimal NewAmount { get; init; }

    public required string Currency { get; init; }
}
