namespace Domain.Models.Configuration;

public sealed class ApiOptions
{
    public const string SectionName = "Api";

    public DocumentationOptions Documentation { get; set; } = new();

    public GraphQlServerOptions GraphQlServer { get; set; } = new();

    public CorsOptions Cors { get; set; } = new();

    public RateLimitOptions RateLimit { get; set; } = new();

    public ForwardedHeadersOptionsModel ForwardedHeaders { get; set; } = new();
}

public sealed class DocumentationOptions
{
    public bool Enabled { get; set; }

    public bool SwaggerUi { get; set; } = true;

    public bool Scalar { get; set; } = true;

    public string SwaggerUiRoute { get; set; } = "swagger";

    public string ScalarRoute { get; set; } = "scalar";

    public string? ContactName { get; set; }

    public Uri? ContactUrl { get; set; }
}

public sealed class GraphQlServerOptions
{
    public bool Enabled { get; set; }

    public string Route { get; set; } = "/graphql";

    public bool EnableTool { get; set; }

    public int MaxExecutionDepth { get; set; } = 10;

    public bool IncludeExceptionDetails { get; set; }

    public GraphQlAutoSchemaOptions AutoSchema { get; set; } = new();
}

public sealed class GraphQlAutoSchemaOptions
{
    public List<string> ExcludeDatabases { get; set; } = [];

    public List<string> ExcludeSchemas { get; set; } = [];

    public List<string> ExcludeTables { get; set; } = [];

    public List<string> ExcludeColumns { get; set; } = [];

    public int DefaultPageSize { get; set; } = 25;

    public int MaxPageSize { get; set; } = 100;

    public int MaxFilterDepth { get; set; } = 5;

    public int MaxInValues { get; set; } = 500;

    public int DocumentSampleSize { get; set; } = 200;

    public int MaxScanItems { get; set; } = 10_000;

    public bool FailOnIntrospectionError { get; set; }
}

public sealed class CorsOptions
{
    public bool Enabled { get; set; } = true;

    public string PolicyName { get; set; } = "DefaultCorsPolicy";

    public List<string> AllowedOrigins { get; set; } = [];

    public List<string> AllowedMethods { get; set; } = [];

    public List<string> AllowedHeaders { get; set; } = [];

    public bool AllowCredentials { get; set; }

    public int PreflightMaxAgeSeconds { get; set; } = 600;
}

public sealed class RateLimitOptions
{
    public bool Enabled { get; set; } = true;

    public string PolicyName { get; set; } = "DefaultRateLimitPolicy";

    public int PermitLimit { get; set; } = 100;

    public int WindowSeconds { get; set; } = 60;

    public int QueueLimit { get; set; }

    public int RejectionStatusCode { get; set; } = 429;
}

public sealed class ForwardedHeadersOptionsModel
{
    public bool Enabled { get; set; } = true;

    public List<string> KnownProxies { get; set; } = [];

    public List<string> KnownNetworks { get; set; } = [];

    public int ForwardLimit { get; set; } = 1;
}
