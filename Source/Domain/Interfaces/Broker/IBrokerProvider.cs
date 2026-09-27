using Domain.Enums;
using Domain.Models.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Domain.Interfaces.Broker;

public interface IBrokerProvider
{
    BrokerType ProviderType { get; }

    Type EventBusType { get; }

    Type ConsumerType { get; }

    void Register(IServiceCollection services, BrokerSettings connection);

    void Validate(BrokerSettings connection);
}