using Domain.Models.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Data.Broker.Providers.RabbitMq;

public sealed class RabbitMqConnectionProvider(
    BrokerSettings settings,
    ILogger<RabbitMqConnectionProvider> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken = default)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_channel is { IsOpen: true })
            {
                return _channel;
            }

            await DisposeChannelAndConnectionAsync().ConfigureAwait(false);

            var factory = new ConnectionFactory
            {
                HostName = settings.Host ?? "localhost",
                Port = settings.Port ?? RabbitMqBrokerProvider.DefaultPort,
                VirtualHost = settings.RabbitMq.VirtualHost,
                UserName = settings.Username ?? "guest",
                Password = settings.Password ?? "guest",

                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
                TopologyRecoveryEnabled = true,
            };

            if (settings.UseSsl)
            {
                factory.Ssl = new SslOption
                {
                    Enabled = true,

                    ServerName = settings.Host ?? string.Empty,
                };
            }

            _connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            await _channel.ExchangeDeclareAsync(
                    exchange: settings.RabbitMq.Exchange,
                    type: ExchangeType.Topic,
                    durable: true,
                    autoDelete: false,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            logger.LogInformation(
                "Connected to RabbitMQ at {Host}:{Port}{VirtualHost}, exchange {Exchange}.",
                settings.Host,
                settings.Port ?? RabbitMqBrokerProvider.DefaultPort,
                settings.RabbitMq.VirtualHost,
                settings.RabbitMq.Exchange);

            return _channel;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeChannelAndConnectionAsync().ConfigureAwait(false);

        _gate.Dispose();

        GC.SuppressFinalize(this);
    }

    private async ValueTask DisposeChannelAndConnectionAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync().ConfigureAwait(false);
            _channel = null;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }
}
