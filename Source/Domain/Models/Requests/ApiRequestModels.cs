using Domain.Enums;

namespace Domain.Models.Requests;

public sealed class RestApiRequestModel
{
    public required string ApiId { get; set; }

    public string? EndpointId { get; set; }

    public string? Path { get; set; }

    public ApiRequestMethod? Method { get; set; }

    public object? Body { get; set; }

    public Dictionary<string, string?> QueryParameters { get; set; } = [];

    public Dictionary<string, string?> RouteParameters { get; set; } = [];

    public Dictionary<string, string?> Headers { get; set; } = [];

    public int TimeoutSeconds { get; set; }
}

public sealed class GraphqlApiRequestModel
{
    public required string ApiId { get; set; }

    public required string Query { get; set; }

    public string? OperationName { get; set; }

    public Dictionary<string, object?> Variables { get; set; } = [];

    public Dictionary<string, string?> Headers { get; set; } = [];

    public int TimeoutSeconds { get; set; }
}
