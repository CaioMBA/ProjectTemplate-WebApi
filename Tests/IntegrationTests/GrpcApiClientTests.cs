using System.Globalization;
using Data.GrpcApi.Channels;
using Data.GrpcApi.Clients;
using Data.GrpcApi.Generated;
using Domain.Enums;
using Domain.Interfaces.Integration;
using Domain.Models.Configuration;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntegrationTests;

public sealed class GreeterService : Greeter.GreeterBase
{
    public const string AuthorizationHeader = "authorization";

    public static string? LastAuthorization { get; private set; }

    public static TimeSpan? LastDeadlineRemaining { get; private set; }

    public override async Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        LastAuthorization = context.RequestHeaders
            .FirstOrDefault(header =>
                string.Equals(header.Key, AuthorizationHeader, StringComparison.OrdinalIgnoreCase))
            ?.Value;

        LastDeadlineRemaining = context.Deadline == DateTime.MaxValue
            ? null
            : context.Deadline - DateTime.UtcNow;

        if (string.Equals(request.Name, "slow", StringComparison.Ordinal))
        {
            await Task.Delay(TimeSpan.FromSeconds(10), context.CancellationToken).ConfigureAwait(false);
        }

        return new HelloReply { Message = $"Hello {request.Name}" };
    }
}

public sealed class GrpcApiClientTests : IAsyncLifetime
{
    private const string ApiId = "GREETER";

    private WebApplication _server = null!;

    private ServiceProvider _services = null!;

    private Uri _address = null!;

    async Task IAsyncLifetime.InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();

        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.Listen(
                System.Net.IPAddress.Loopback,
                0,
                listen => listen.Protocols = HttpProtocols.Http2));

        builder.Logging.ClearProviders();

        builder.Services.AddGrpc();

        _server = builder.Build();

        _server.MapGrpcService<GreeterService>();

        await _server.StartAsync().ConfigureAwait(false);

        var addresses = _server.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()
            ?.Addresses ?? throw new InvalidOperationException("Kestrel exposed no addresses.");

        _address = new Uri(addresses.First());

        _services = BuildClientContainer(_address, timeoutSeconds: 5);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _services.DisposeAsync().ConfigureAwait(false);

        await _server.StopAsync().ConfigureAwait(false);

        await _server.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public void GetClientReturnsAWorkingGeneratedClient()
    {
        var client = _services.GetRequiredService<IGrpcApiClient>()
            .GetClient<Greeter.GreeterClient>(ApiId);

        client.ShouldNotBeNull();
        client.ShouldBeOfType<Greeter.GreeterClient>();
    }

    [Fact]
    public async Task TheClientReachesTheServerAndReturnsTheResponse()
    {
        var client = _services.GetRequiredService<IGrpcApiClient>()
            .GetClient<Greeter.GreeterClient>(ApiId);

        var reply = await client.SayHelloAsync(new HelloRequest { Name = "template" });

        reply.Message.ShouldBe("Hello template");
    }

    [Fact]
    public async Task ConfiguredAuthorizationReachesTheServer()
    {
        var client = _services.GetRequiredService<IGrpcApiClient>()
            .GetClient<Greeter.GreeterClient>(ApiId);

        await client.SayHelloAsync(new HelloRequest { Name = "authorized" });

        GreeterService.LastAuthorization.ShouldBe("Bearer grpc-token");
    }

    [Fact]
    public async Task TheConfiguredTimeoutBecomesACallDeadline()
    {
        var client = _services.GetRequiredService<IGrpcApiClient>()
            .GetClient<Greeter.GreeterClient>(ApiId);

        await client.SayHelloAsync(new HelloRequest { Name = "deadline" });

        var remaining = GreeterService.LastDeadlineRemaining.ShouldNotBeNull();

        remaining.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(5));
        remaining.ShouldBeGreaterThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ACallThatOutlivesTheDeadlineIsCancelled()
    {
        var shortTimeout = BuildClientContainer(_address, timeoutSeconds: 1);

        await using (shortTimeout.ConfigureAwait(false))
        {
            var client = shortTimeout.GetRequiredService<IGrpcApiClient>()
                .GetClient<Greeter.GreeterClient>(ApiId);

            var exception = await Should.ThrowAsync<RpcException>(
                async () => await client.SayHelloAsync(new HelloRequest { Name = "slow" }));

            exception.StatusCode.ShouldBe(StatusCode.DeadlineExceeded);
        }
    }

    [Fact]
    public async Task AProtocolMismatchIsRejected()
    {
        var restOnly = BuildClientContainer(_address, timeoutSeconds: 5, protocol: ApiProtocolType.Rest);

        await using (restOnly.ConfigureAwait(false))
        {
            var client = restOnly.GetRequiredService<IGrpcApiClient>();

            Should.Throw<InvalidOperationException>(() => client.GetClient<Greeter.GreeterClient>(ApiId))
                .Message.ShouldContain("not Grpc");
        }
    }

    [Fact]
    public async Task AMissingBaseAddressIsRejected()
    {
        var noAddress = BuildClientContainer(baseAddress: null, timeoutSeconds: 5);

        await using (noAddress.ConfigureAwait(false))
        {
            var client = noAddress.GetRequiredService<IGrpcApiClient>();

            Should.Throw<InvalidOperationException>(() => client.GetClient<Greeter.GreeterClient>(ApiId))
                .Message.ShouldContain("no BaseAddress");
        }
    }

    [Fact]
    public void ATypeThatIsNotAGeneratedClientIsRejected()
    {
        var client = _services.GetRequiredService<IGrpcApiClient>();

        Should.Throw<InvalidOperationException>(() => client.GetClient<System.Text.StringBuilder>(ApiId))
            .Message.ShouldContain("not a generated gRPC client");
    }

    private static ServiceProvider BuildClientContainer(
        Uri? baseAddress,
        int timeoutSeconds,
        ApiProtocolType protocol = ApiProtocolType.Grpc)
    {
        var settings = new AppSettings
        {
            Apis =
            [
                new ApiSettings
                {
                    Id = ApiId,
                    Protocol = protocol,
                    BaseAddress = baseAddress,
                    AuthorizationType = ApiAuthorizationType.Bearer,
                    AuthorizationValue = "grpc-token",
                    TimeoutSeconds = timeoutSeconds,
                },
            ],
        };

        var collection = new ServiceCollection();

        collection.AddLogging();
        collection.AddHttpClient();
        collection.AddSingleton<IOptionsMonitor<AppSettings>>(
            new StaticOptionsMonitor<AppSettings>(settings));
        collection.AddSingleton(TimeProvider.System);
        collection.AddSingleton<GrpcChannelPool>();
        collection.AddScoped<IGrpcApiClient, GrpcApiClient>();

        return collection.BuildServiceProvider();
    }
}

internal sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name) => value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
