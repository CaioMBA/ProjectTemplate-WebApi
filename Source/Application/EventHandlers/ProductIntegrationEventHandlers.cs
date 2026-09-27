using Domain.Events.Integration;
using Domain.Interfaces.Integration;
using Microsoft.Extensions.Logging;

namespace Application.EventHandlers;

public sealed class ProductCreatedIntegrationEventHandler(
    ILogger<ProductCreatedIntegrationEventHandler> logger)
    : IIntegrationEventHandler<ProductCreatedIntegrationEvent>
{
    public Task Handle(
        ProductCreatedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        logger.LogInformation(
            "Consumed {EventType} for product {ProductId} ({Sku}).",
            integrationEvent.EventType,
            integrationEvent.ProductId,
            integrationEvent.Sku);

        return Task.CompletedTask;
    }
}

public sealed class ProductPriceChangedIntegrationEventHandler(
    ILogger<ProductPriceChangedIntegrationEventHandler> logger)
    : IIntegrationEventHandler<ProductPriceChangedIntegrationEvent>
{
    public Task Handle(
        ProductPriceChangedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        logger.LogInformation(
            "Consumed {EventType} for product {ProductId}: {PreviousAmount} -> {NewAmount} {Currency}.",
            integrationEvent.EventType,
            integrationEvent.ProductId,
            integrationEvent.PreviousAmount,
            integrationEvent.NewAmount,
            integrationEvent.Currency);

        return Task.CompletedTask;
    }
}
