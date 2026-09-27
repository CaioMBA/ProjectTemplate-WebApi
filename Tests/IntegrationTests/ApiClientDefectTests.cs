using Data.GraphqlApi.Clients;
using Data.RestApi.Clients;
using Domain.Enums;
using Domain.Interfaces.Integration;
using Domain.Models.Configuration;
using Domain.Models.Requests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IntegrationTests;

public sealed class ApiClientDefectTests
{
    private const string ApiId = "PROBE";

    private static ServiceProvider Build(ApiSettings api)
    {
        var collection = new ServiceCollection();

        collection.AddLogging();
        collection.AddHttpClient();
        collection.AddSingleton<IOptionsMonitor<AppSettings>>(
            new StaticOptionsMonitor<AppSettings>(new AppSettings { Apis = [api] }));
        collection.AddScoped<IRestApiClient, RestApiClient>();
        collection.AddScoped<IGraphqlApiClient, GraphqlApiClient>();

        return collection.BuildServiceProvider();
    }

    private static ApiSettings Api(
        ApiProtocolType protocol = ApiProtocolType.Rest,
        Uri? baseAddress = null) =>
        new()
        {
            Id = ApiId,
            Protocol = protocol,
            BaseAddress = baseAddress,
            TimeoutSeconds = 5,
            Endpoints =
            [
                new ApiEndpointSettings
                {
                    Id = "PROBE_ENDPOINT",
                    Path = "/probe",
                    Method = ApiRequestMethod.Get,
                },
            ],
        };

    [Fact]
    public async Task RestReturnsAFailureInsteadOfThrowingWhenBaseAddressIsMissing()
    {
        using var provider = Build(Api(baseAddress: null));

        var result = await provider.GetRequiredService<IRestApiClient>()
            .SendRawAsync(new RestApiRequestModel { ApiId = ApiId, EndpointId = "PROBE_ENDPOINT" });

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("RestApi.MissingBaseAddress");
    }

    [Fact]
    public async Task RestRejectsAnApiConfiguredForAnotherProtocol()
    {
        using var provider = Build(Api(ApiProtocolType.Grpc, new Uri("https://localhost:1")));

        var result = await provider.GetRequiredService<IRestApiClient>()
            .SendRawAsync(new RestApiRequestModel { ApiId = ApiId, EndpointId = "PROBE_ENDPOINT" });

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("RestApi.ProtocolMismatch");
    }

    [Fact]
    public async Task GraphqlRejectsAnApiConfiguredForAnotherProtocol()
    {
        using var provider = Build(Api(ApiProtocolType.Rest, new Uri("https://localhost:1")));

        var result = await provider.GetRequiredService<IGraphqlApiClient>()
            .QueryAsync<string>(new GraphqlApiRequestModel { ApiId = ApiId, Query = "{ probe }" });

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("GraphqlApi.ProtocolMismatch");
    }

    [Fact]
    public async Task GraphqlReturnsAFailureInsteadOfThrowingWhenBaseAddressIsMissing()
    {
        using var provider = Build(Api(ApiProtocolType.GraphQl, baseAddress: null));

        var result = await provider.GetRequiredService<IGraphqlApiClient>()
            .QueryAsync<string>(new GraphqlApiRequestModel { ApiId = ApiId, Query = "{ probe }" });

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("GraphqlApi.MissingBaseAddress");
    }

    [Fact]
    public async Task GraphqlTurnsAnUnreachableServerIntoATransportFailure()
    {
        using var provider = Build(
            Api(ApiProtocolType.GraphQl, new Uri("http://127.0.0.1:1/graphql")));

        var result = await provider.GetRequiredService<IGraphqlApiClient>()
            .QueryAsync<string>(new GraphqlApiRequestModel { ApiId = ApiId, Query = "{ probe }" });

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBeOneOf("GraphqlApi.TransportFailure", "GraphqlApi.Timeout");
    }

    [Fact]
    public async Task AnUnknownApiIdIsAFailureForBothClients()
    {
        using var provider = Build(Api(baseAddress: new Uri("https://localhost:1")));

        var rest = await provider.GetRequiredService<IRestApiClient>()
            .SendRawAsync(new RestApiRequestModel { ApiId = "MISSING", Path = "/probe" });

        var graphql = await provider.GetRequiredService<IGraphqlApiClient>()
            .QueryAsync<string>(new GraphqlApiRequestModel { ApiId = "MISSING", Query = "{ probe }" });

        rest.Error.Code.ShouldBe("RestApi.UnknownDependency");
        graphql.Error.Code.ShouldBe("GraphqlApi.UnknownDependency");
    }
}
