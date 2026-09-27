using System.Diagnostics;
using System.Text.Json;
using Domain.Abstractions;
using Domain.Enums;
using Domain.Extensions;
using Domain.Integration;
using Domain.Interfaces.Integration;
using Domain.Models.Configuration;
using Domain.Models.Requests;
using Domain.Results;
using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.SystemTextJson;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Data.GraphqlApi.Clients;

public sealed class GraphqlApiClient(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<AppSettings> settingsMonitor,
    ILogger<GraphqlApiClient> logger) : IGraphqlApiClient
{
    private const string QueryOperation = "query";

    private const string MutationOperation = "mutation";

    private static readonly ActivitySource _activitySource = new(TelemetryNames.IntegrationActivitySource);

    private static readonly JsonSerializerOptions _serializerOptions = new(JsonDefaults.Standard);

    public Task<Result<TResponse>> QueryAsync<TResponse>(
        GraphqlApiRequestModel request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync<TResponse>(request, QueryOperation, cancellationToken);

    public Task<Result<TResponse>> MutateAsync<TResponse>(
        GraphqlApiRequestModel request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync<TResponse>(request, MutationOperation, cancellationToken);

    private async Task<Result<TResponse>> ExecuteAsync<TResponse>(
        GraphqlApiRequestModel request,
        string operationKind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = settingsMonitor.CurrentValue;

        ApiSettings api;

        try
        {
            api = settings.GetApi(request.ApiId);
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<TResponse>(Error.Failure("GraphqlApi.UnknownDependency", exception.Message));
        }

        if (api.Protocol != ApiProtocolType.GraphQl)
        {
            return Result.Failure<TResponse>(Error.Failure(
                "GraphqlApi.ProtocolMismatch",
                $"API '{request.ApiId}' is configured as {api.Protocol}, not GraphQl. " +
                $"Use IRestApiClient or IGrpcApiClient instead."));
        }

        if (api.BaseAddress is null)
        {
            return Result.Failure<TResponse>(Error.Failure(
                "GraphqlApi.MissingBaseAddress",
                $"API '{request.ApiId}' has no BaseAddress configured."));
        }

        using var activity = _activitySource.StartActivity(
            $"GraphQL {operationKind} {request.ApiId}",
            ActivityKind.Client);

        using var client = CreateClient(api, request);

        var graphRequest = new GraphQLRequest
        {
            Query = request.Query,
            OperationName = request.OperationName,

            Variables = request.Variables.Count > 0 ? request.Variables : null,
        };

        var timeoutSeconds = request.TimeoutSeconds > 0 ? request.TimeoutSeconds : api.TimeoutSeconds;

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeoutSource.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            var response = await Execute<TResponse>(client, graphRequest, operationKind, timeoutSource.Token)
                .ConfigureAwait(false);

            if (response.Errors is { Length: > 0 })
            {
                var description = string.Join("; ", response.Errors.Select(error => error.Message));

                logger.LogWarning(
                    "GraphQL {OperationKind} against {ApiId} returned {ErrorCount} error(s).",
                    operationKind,
                    request.ApiId,
                    response.Errors.Length);

                return Result.Failure<TResponse>(Error.Failure("GraphqlApi.QueryErrors", description));
            }

            return response.Data is null
                ? Result.Failure<TResponse>(Error.Failure(
                    "GraphqlApi.EmptyData",
                    $"'{request.ApiId}' returned no data and no errors."))
                : Result.Success(response.Data);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Timeout");

            logger.LogWarning(
                exception,
                "The GraphQL {OperationKind} against {ApiId} timed out after {TimeoutSeconds}s.",
                operationKind,
                request.ApiId,
                timeoutSeconds);

            return Result.Failure<TResponse>(Error.Unavailable(
                "GraphqlApi.Timeout",
                $"'{request.ApiId}' did not respond within {timeoutSeconds} seconds."));
        }
        catch (Exception exception) when (exception is GraphQLHttpRequestException or HttpRequestException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);

            logger.LogError(exception, "The GraphQL call to {ApiId} failed at transport level.", request.ApiId);

            return Result.Failure<TResponse>(Error.Unavailable(
                "GraphqlApi.TransportFailure",
                $"'{request.ApiId}' could not be reached."));
        }
        catch (JsonException exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);

            logger.LogError(exception, "The GraphQL response from {ApiId} could not be read.", request.ApiId);

            return Result.Failure<TResponse>(Error.Failure(
                "GraphqlApi.DeserialisationFailed",
                $"The body returned by '{request.ApiId}' did not match the expected shape."));
        }
    }

    private static Task<GraphQLResponse<TResponse>> Execute<TResponse>(
        GraphQLHttpClient client,
        GraphQLRequest request,
        string operationKind,
        CancellationToken cancellationToken) =>
        string.Equals(operationKind, MutationOperation, StringComparison.Ordinal)
            ? client.SendMutationAsync<TResponse>(request, cancellationToken)
            : client.SendQueryAsync<TResponse>(request, cancellationToken);

    private GraphQLHttpClient CreateClient(ApiSettings api, GraphqlApiRequestModel request)
    {
        var httpClient = httpClientFactory.CreateClient(NamedHttpClient.GraphqlApi.ToString());

        var options = new GraphQLHttpClientOptions { EndPoint = api.BaseAddress };

        var client = new GraphQLHttpClient(
            options,
            new SystemTextJsonSerializer(_serializerOptions),
            httpClient);

        ApiAuthentication.Apply(api, httpClient.DefaultRequestHeaders);

        foreach (var (key, value) in request.Headers)
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
        }

        return client;
    }
}
