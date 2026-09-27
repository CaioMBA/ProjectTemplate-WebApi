using System.Collections.Concurrent;
using Data.GrpcApi.Interceptors;
using Domain.Enums;
using Domain.Integration;
using Domain.Models.Configuration;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;

namespace Data.GrpcApi.Channels;

public sealed class GrpcChannelPool(
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory) : IAsyncDisposable
{
    private readonly ILogger<GrpcChannelPool> _logger = loggerFactory.CreateLogger<GrpcChannelPool>();

    private readonly ConcurrentDictionary<string, Lazy<PooledChannel>> _channels =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _disposed;

    public CallInvoker GetInvoker(ApiSettings api)
    {
        ArgumentNullException.ThrowIfNull(api);

        ObjectDisposedException.ThrowIf(_disposed, this);

        if (api.Protocol != ApiProtocolType.Grpc)
        {
            throw new InvalidOperationException(
                $"API '{api.Id}' is configured as {api.Protocol}, not Grpc. " +
                $"Use IRestApiClient or IGraphqlApiClient instead.");
        }

        if (api.BaseAddress is null)
        {
            throw new InvalidOperationException(
                $"API '{api.Id}' has no BaseAddress configured. " +
                $"Set Settings:Apis for '{api.Id}' to the gRPC server address.");
        }

        var pooled = _channels
            .GetOrAdd(api.Id, _ => new Lazy<PooledChannel>(() => Create(api)))
            .Value;

        return pooled.Invoker;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        var created = _channels.Values
            .Where(entry => entry.IsValueCreated)
            .Select(entry => entry.Value.Channel);

        foreach (var channel in created)
        {
            await channel.ShutdownAsync().ConfigureAwait(false);

            channel.Dispose();
        }

        _channels.Clear();
    }

    private PooledChannel Create(ApiSettings api)
    {
        var httpClient = httpClientFactory.CreateClient(NamedHttpClient.GrpcApi.ToString());

        ApiAuthentication.Apply(api, httpClient.DefaultRequestHeaders);

        httpClient.Timeout = Timeout.InfiniteTimeSpan;

        var channel = GrpcChannel.ForAddress(
            api.BaseAddress!,
            new GrpcChannelOptions
            {
                HttpClient = httpClient,
                DisposeHttpClient = true,
                LoggerFactory = loggerFactory,
            });

        _logger.LogInformation(
            "Opened a gRPC channel to {ApiId} at {BaseAddress}.",
            api.Id,
            api.BaseAddress);

        var invoker = channel.Intercept(
            new DeadlineInterceptor(TimeSpan.FromSeconds(api.TimeoutSeconds), timeProvider));

        return new PooledChannel(channel, invoker);
    }

    private sealed record PooledChannel(GrpcChannel Channel, CallInvoker Invoker);
}
