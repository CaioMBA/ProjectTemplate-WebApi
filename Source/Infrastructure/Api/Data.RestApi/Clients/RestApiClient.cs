using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using Domain.Abstractions;
using Domain.Enums;
using Domain.Extensions;
using Domain.Integration;
using Domain.Interfaces.Integration;
using Domain.Models.Configuration;
using Domain.Models.Requests;
using Domain.Models.Responses;
using Domain.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Data.RestApi.Clients;

public sealed class RestApiClient(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<AppSettings> settingsMonitor,
    ILogger<RestApiClient> logger) : IRestApiClient
{
    private static readonly ActivitySource _activitySource = new(TelemetryNames.IntegrationActivitySource);

    public async Task<Result<TResponse>> SendAsync<TResponse>(
        RestApiRequestModel request,
        CancellationToken cancellationToken = default)
    {
        var raw = await SendRawAsync(request, cancellationToken).ConfigureAwait(false);

        if (raw.IsFailure)
        {
            return Result.Failure<TResponse>(raw.Error);
        }

        if (string.IsNullOrWhiteSpace(raw.Value.Content))
        {
            return Result.Failure<TResponse>(Error.Failure(
                "RestApi.EmptyBody",
                $"'{request.ApiId}' returned {(int)raw.Value.StatusCode} with an empty body."));
        }

        try
        {
            var deserialised = raw.Value.Content.ToObject<TResponse>();

            return deserialised is null
                ? Result.Failure<TResponse>(Error.Failure(
                    "RestApi.DeserialisationFailed",
                    $"The body returned by '{request.ApiId}' deserialised to null."))
                : Result.Success(deserialised);
        }
        catch (System.Text.Json.JsonException exception)
        {
            logger.LogError(
                exception,
                "Failed to deserialise the response from {ApiId} into {ResponseType}.",
                request.ApiId,
                typeof(TResponse).Name);

            return Result.Failure<TResponse>(Error.Failure(
                "RestApi.DeserialisationFailed",
                $"The body returned by '{request.ApiId}' did not match the expected shape."));
        }
    }

    public async Task<Result<RestApiResponseModel>> SendRawAsync(
        RestApiRequestModel request,
        CancellationToken cancellationToken = default)
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
            return Result.Failure<RestApiResponseModel>(Error.Failure(
                "RestApi.UnknownDependency",
                exception.Message));
        }

        if (api.Protocol != ApiProtocolType.Rest)
        {
            return Result.Failure<RestApiResponseModel>(Error.Failure(
                "RestApi.ProtocolMismatch",
                $"API '{request.ApiId}' is configured as {api.Protocol}, not Rest. " +
                $"Use IGraphqlApiClient or IGrpcApiClient instead."));
        }

        if (api.BaseAddress is null)
        {
            return Result.Failure<RestApiResponseModel>(Error.Failure(
                "RestApi.MissingBaseAddress",
                $"API '{request.ApiId}' has no BaseAddress configured."));
        }

        var endpoint = ResolveEndpoint(api, request);

        if (endpoint.IsFailure)
        {
            return Result.Failure<RestApiResponseModel>(endpoint.Error);
        }

        using var activity = _activitySource.StartActivity(
            $"REST {request.ApiId}/{request.EndpointId ?? request.Path}",
            ActivityKind.Client);

        var client = httpClientFactory.CreateClient(NamedHttpClient.RestApi.ToString());

        var timeoutSeconds = api.TimeoutSeconds;

        if (endpoint.Value.TimeoutSeconds > 0)
        {
            timeoutSeconds = endpoint.Value.TimeoutSeconds;
        }

        if (request.TimeoutSeconds > 0)
        {
            timeoutSeconds = request.TimeoutSeconds;
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        using var message = BuildRequest(api, api.BaseAddress, endpoint.Value, request);

        var timestamp = Stopwatch.GetTimestamp();

        try
        {
            using var response = await client
                .SendAsync(message, timeoutSource.Token)
                .ConfigureAwait(false);

            var content = await response.Content
                .ReadAsStringAsync(timeoutSource.Token)
                .ConfigureAwait(false);

            activity?.SetTag("http.response.status_code", (int)response.StatusCode);

            var model = new RestApiResponseModel
            {
                StatusCode = response.StatusCode,
                IsSuccessStatusCode = response.IsSuccessStatusCode,
                Content = content,
                ContentType = response.Content.Headers.ContentType?.ToString(),
                Elapsed = Stopwatch.GetElapsedTime(timestamp),
                Headers = response.Headers.ToDictionary(
                    header => header.Key,
                    header => string.Join(", ", header.Value),
                    StringComparer.OrdinalIgnoreCase),
            };

            if (response.IsSuccessStatusCode)
            {
                return Result.Success(model);
            }

            var errorType = (int)response.StatusCode switch
            {
                400 or 422 => ErrorType.Validation,
                401 => ErrorType.Unauthorized,
                403 => ErrorType.Forbidden,
                404 => ErrorType.NotFound,
                409 => ErrorType.Conflict,
                >= 500 => ErrorType.Unavailable,
                _ => ErrorType.Failure,
            };

            return Result.Failure<RestApiResponseModel>(new Error(
                $"RestApi.{(int)response.StatusCode}",
                $"'{request.ApiId}' returned {(int)response.StatusCode} {response.ReasonPhrase}.",
                errorType));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Timed out");

            return Result.Failure<RestApiResponseModel>(Error.Unavailable(
                "RestApi.Timeout",
                $"'{request.ApiId}' did not respond within {timeoutSeconds}s."));
        }
        catch (HttpRequestException exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);

            logger.LogError(exception, "The call to {ApiId} failed at transport level.", request.ApiId);

            return Result.Failure<RestApiResponseModel>(Error.Unavailable(
                "RestApi.TransportFailure",
                $"'{request.ApiId}' could not be reached."));
        }
    }

    private static Result<ApiEndpointSettings> ResolveEndpoint(
        ApiSettings api,
        RestApiRequestModel request)
    {
        if (!string.IsNullOrWhiteSpace(request.EndpointId))
        {
            var configured = api.Endpoints.Find(candidate =>
                string.Equals(candidate.Id, request.EndpointId, StringComparison.OrdinalIgnoreCase));

            return configured is null
                ? Result.Failure<ApiEndpointSettings>(Error.Failure(
                    "RestApi.UnknownEndpoint",
                    $"API '{request.ApiId}' has no endpoint '{request.EndpointId}'."))
                : Result.Success(configured);
        }

        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return Result.Failure<ApiEndpointSettings>(Error.Validation(
                "RestApi.PathRequired",
                "Either EndpointId or Path must be supplied."));
        }

        return Result.Success(new ApiEndpointSettings
        {
            Id = "ad-hoc",
            Path = request.Path,
            Method = request.Method ?? ApiRequestMethod.Get,
        });
    }

    private static HttpRequestMessage BuildRequest(
        ApiSettings api,
        Uri baseAddress,
        ApiEndpointSettings endpoint,
        RestApiRequestModel request)
    {
        var path = endpoint.Path;

        foreach (var (key, value) in request.RouteParameters)
        {
            path = path.Replace($"{{{key}}}", Uri.EscapeDataString(value ?? string.Empty), StringComparison.Ordinal);
        }

        if (request.QueryParameters.Count > 0)
        {
            var query = new StringBuilder();

            foreach (var (key, value) in request.QueryParameters)
            {
                query
                    .Append(query.Length == 0 ? '?' : '&')
                    .Append(Uri.EscapeDataString(key))
                    .Append('=')
                    .Append(Uri.EscapeDataString(value ?? string.Empty));
            }

            path += query.ToString();
        }

        var method = (request.Method ?? endpoint.Method) switch
        {
            ApiRequestMethod.Post => HttpMethod.Post,
            ApiRequestMethod.Put => HttpMethod.Put,
            ApiRequestMethod.Patch => HttpMethod.Patch,
            ApiRequestMethod.Delete => HttpMethod.Delete,
            ApiRequestMethod.Head => HttpMethod.Head,
            ApiRequestMethod.Options => HttpMethod.Options,
            _ => HttpMethod.Get,
        };

        var message = new HttpRequestMessage(method, new Uri(baseAddress, path.TrimStart('/')));

        if (request.Body is not null)
        {
            message.Content = JsonContent.Create(request.Body, options: JsonDefaults.Standard);
        }

        ApplyAuthorization(api, message);

        foreach (var (key, value) in request.Headers)
        {
            message.Headers.TryAddWithoutValidation(key, value);
        }

        return message;
    }

    private static void ApplyAuthorization(ApiSettings api, HttpRequestMessage message) =>
        ApiAuthentication.Apply(api, message.Headers);
}
