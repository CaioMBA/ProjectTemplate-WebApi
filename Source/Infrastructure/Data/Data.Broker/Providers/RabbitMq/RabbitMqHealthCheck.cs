using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Data.Broker.Providers.RabbitMq;

public sealed class RabbitMqHealthCheck(RabbitMqConnectionProvider connectionProvider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var channel = await connectionProvider
                .GetChannelAsync(cancellationToken)
                .ConfigureAwait(false);

            return channel.IsOpen
                ? HealthCheckResult.Healthy("The AMQP channel is open.")
                : HealthCheckResult.Degraded("The AMQP channel is closed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return HealthCheckResult.Degraded("The broker is unreachable.", exception);
        }
    }
}
