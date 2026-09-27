using Domain.Enums;
using Domain.Interfaces.Broker;

namespace Data.Broker.Providers;

public static class BrokerProviderRegistry
{
    private static readonly IBrokerProvider[] _providers =
    [
        new RabbitMqBrokerProvider(),
        new KafkaBrokerProvider(),
    ];

    public static IReadOnlyList<IBrokerProvider> All => _providers;

    public static IBrokerProvider Resolve(BrokerType providerType) =>
        _providers.SingleOrDefault(provider => provider.ProviderType == providerType)
        ?? throw new NotSupportedException(
            $"BrokerType '{providerType}' has no IBrokerProvider. "
            + $"Registered providers: {string.Join(", ", _providers.Select(p => p.ProviderType))}.");
}
