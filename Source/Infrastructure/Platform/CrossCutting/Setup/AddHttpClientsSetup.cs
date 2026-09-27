using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace CrossCutting.Setup;

public static class HttpClientsSetup
{
    private static readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(100);

    private static readonly TimeSpan _dnsRefreshInterval = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan _idleConnectionTimeout = TimeSpan.FromMinutes(1);

    private const string SupportedContentEncodings = "br, gzip, deflate";

    public static IServiceCollection AddHttpClientsSetup(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.ConfigureHttpClientDefaults(builder =>
        {
            builder.ConfigureHttpClient(client =>
            {
                client.Timeout = _requestTimeout;

                client.DefaultRequestVersion = HttpVersion.Version30;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;

                client.DefaultRequestHeaders.AcceptEncoding.ParseAdd(SupportedContentEncodings);
            });

            builder.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,

                PooledConnectionLifetime = _dnsRefreshInterval,
                PooledConnectionIdleTimeout = _idleConnectionTimeout,
                EnableMultipleHttp2Connections = true,
            });

            builder.AddStandardResilienceHandler();
        });

        return services;
    }
}
