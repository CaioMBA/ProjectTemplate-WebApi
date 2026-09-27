using Domain.Abstractions;

namespace Domain.Events;

public sealed record ProductCreatedDomainEvent(Guid ProductId, string Sku, string Name) : DomainEvent;

public sealed record ProductPriceChangedDomainEvent(
    Guid ProductId,
    string Sku,
    decimal PreviousAmount,
    decimal NewAmount,
    string Currency) : DomainEvent;
