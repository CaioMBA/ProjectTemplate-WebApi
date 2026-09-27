using System.Net;
using Domain.Models.Configuration;
using Microsoft.AspNetCore.HttpOverrides;

namespace WebApi.Setup;

public static class ForwardedHeadersSetup
{
    public static IServiceCollection AddForwardedHeadersSetup(
        this IServiceCollection services,
        ApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var forwarded = options.ForwardedHeaders;

        if (!forwarded.Enabled)
        {
            return services;
        }

        services.Configure<ForwardedHeadersOptions>(headers =>
        {
            headers.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;

            headers.ForwardLimit = forwarded.ForwardLimit;

            headers.KnownProxies.Clear();

            headers.KnownIPNetworks.Clear();

            foreach (var proxy in forwarded.KnownProxies)
            {
                if (IPAddress.TryParse(proxy, out var address))
                {
                    headers.KnownProxies.Add(address);
                }
            }

            foreach (var network in forwarded.KnownNetworks)
            {
                if (System.Net.IPNetwork.TryParse(network, out var parsed))
                {
                    headers.KnownIPNetworks.Add(parsed);
                }
            }
        });

        return services;
    }
}
