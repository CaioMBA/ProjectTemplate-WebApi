using System.Collections.Concurrent;
using System.Reflection;
using Data.GrpcApi.Channels;
using Domain.Interfaces.Integration;
using Domain.Models.Configuration;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace Data.GrpcApi.Clients;

public sealed class GrpcApiClient(
    GrpcChannelPool channelPool,
    IOptionsMonitor<AppSettings> settingsMonitor) : IGrpcApiClient
{
    private static readonly ConcurrentDictionary<Type, ConstructorInfo> _constructors = new();

    public TClient GetClient<TClient>(string apiId)
        where TClient : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiId);

        var api = settingsMonitor.CurrentValue.GetApi(apiId);

        var invoker = channelPool.GetInvoker(api);

        var constructor = _constructors.GetOrAdd(typeof(TClient), ResolveConstructor);

        return (TClient)constructor.Invoke([invoker]);
    }

    private static ConstructorInfo ResolveConstructor(Type clientType) =>
        clientType.GetConstructor([typeof(CallInvoker)])
        ?? throw new InvalidOperationException(
            $"'{clientType.FullName}' is not a generated gRPC client. " +
            $"Expected a public constructor taking a single {nameof(CallInvoker)}. " +
            $"Add the service definition to Data.GrpcApi/Protos and rebuild so protoc " +
            $"generates the client, then pass the generated type to GetClient.");
}
