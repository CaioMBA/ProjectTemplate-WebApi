using Confluent.Kafka;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Data.Broker.Providers.Kafka;

public sealed class KafkaHealthCheck(IProducer<string, byte[]> producer) : IHealthCheck
{
    private static readonly TimeSpan _metadataTimeout = TimeSpan.FromSeconds(5);

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var admin = new DependentAdminClientBuilder(producer.Handle).Build();

            var metadata = admin.GetMetadata(_metadataTimeout);

            return Task.FromResult(metadata.Brokers.Count > 0
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Kafka reported no reachable brokers."));
        }
        catch (KafkaException exception)
        {
            return Task.FromResult(
                HealthCheckResult.Unhealthy("Kafka metadata request failed.", exception));
        }
    }
}
