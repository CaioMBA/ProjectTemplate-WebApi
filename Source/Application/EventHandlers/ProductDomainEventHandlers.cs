using Domain.Events;
using Domain.Events.Integration;
using Domain.Interfaces.Messaging;
using Domain.Interfaces.Persistence;
using Domain.Models.Requests.Products;
using Microsoft.Extensions.DependencyInjection;

namespace Application.EventHandlers;

public sealed class ProductCreatedDomainEventHandler(
    [FromKeyedServices(ProductsStore.DatabaseId)] IOutboxWriter outbox)
    : IDomainEventHandler<ProductCreatedDomainEvent>
{
    public Task Handle(ProductCreatedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return outbox.EnqueueAsync(
            new ProductCreatedIntegrationEvent
            {
                OccurredOnUtc = domainEvent.OccurredOnUtc,
                ProductId = domainEvent.ProductId,
                Sku = domainEvent.Sku,
                ProductName = domainEvent.Name,
            },
            cancellationToken);
    }
}

public sealed class ProductPriceChangedDomainEventHandler(
    [FromKeyedServices(ProductsStore.DatabaseId)] IOutboxWriter outbox)
    : IDomainEventHandler<ProductPriceChangedDomainEvent>
{
    public Task Handle(ProductPriceChangedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return outbox.EnqueueAsync(
            new ProductPriceChangedIntegrationEvent
            {
                OccurredOnUtc = domainEvent.OccurredOnUtc,
                ProductId = domainEvent.ProductId,
                Sku = domainEvent.Sku,
                PreviousAmount = domainEvent.PreviousAmount,
                NewAmount = domainEvent.NewAmount,
                Currency = domainEvent.Currency,
            },
            cancellationToken);
    }
}
