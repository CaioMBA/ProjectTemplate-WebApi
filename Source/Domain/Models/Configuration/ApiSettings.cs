using Domain.Attributes;
using Domain.Enums;

namespace Domain.Models.Configuration;

public sealed class ApiSettings
{
    public required string Id { get; set; }

    public ApiProtocolType Protocol { get; set; } = ApiProtocolType.Rest;

    public Uri? BaseAddress { get; set; }

    public ApiAuthorizationType AuthorizationType { get; set; } = ApiAuthorizationType.None;

    [Secret]
    public string? AuthorizationValue { get; set; }

    public string ApiKeyHeaderName { get; set; } = "X-Api-Key";

    public int TimeoutSeconds { get; set; } = 30;

    public string? HealthEndpoint { get; set; } = "/health";

    public List<ApiEndpointSettings> Endpoints { get; set; } = [];
}

public sealed class ApiEndpointSettings
{
    public required string Id { get; set; }

    public required string Path { get; set; }

    public ApiRequestMethod Method { get; set; } = ApiRequestMethod.Get;

    public int TimeoutSeconds { get; set; }
}
