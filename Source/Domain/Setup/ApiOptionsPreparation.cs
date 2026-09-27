using System.Net;
using Domain.Models.Configuration;

namespace Domain.Setup;

public static class ApiOptionsPreparation
{
    public static IReadOnlyList<string> Validate(ApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        const string root = ApiOptions.SectionName;
        var errors = new List<string>();

        var documentation = options.Documentation;

        if (documentation.Enabled && documentation.SwaggerUi && string.IsNullOrWhiteSpace(documentation.SwaggerUiRoute))
        {
            errors.Add($"{root}:Documentation:SwaggerUiRoute is required when SwaggerUi is on.");
        }

        if (documentation.Enabled && documentation.Scalar && string.IsNullOrWhiteSpace(documentation.ScalarRoute))
        {
            errors.Add($"{root}:Documentation:ScalarRoute is required when Scalar is on.");
        }

        var graphQl = options.GraphQlServer;

        if (graphQl.Enabled && (string.IsNullOrWhiteSpace(graphQl.Route) || graphQl.Route[0] != '/'))
        {
            errors.Add($"{root}:GraphQlServer:Route '{graphQl.Route}' must start with '/'.");
        }

        if (graphQl.MaxExecutionDepth < 1)
        {
            errors.Add($"{root}:GraphQlServer:MaxExecutionDepth must be at least 1.");
        }

        errors.AddRange(ValidateAutoSchema(graphQl.AutoSchema, $"{root}:GraphQlServer:AutoSchema"));

        var cors = options.Cors;

        if (cors.PreflightMaxAgeSeconds < 0)
        {
            errors.Add($"{root}:Cors:PreflightMaxAgeSeconds must not be negative.");
        }

        if (cors.AllowCredentials && cors.AllowedOrigins.Contains("*"))
        {
            errors.Add($"{root}:Cors:AllowCredentials cannot be combined with the '*' origin; list the origins.");
        }

        var rateLimit = options.RateLimit;

        if (rateLimit.Enabled)
        {
            if (rateLimit.PermitLimit < 1)
            {
                errors.Add($"{root}:RateLimit:PermitLimit must be at least 1.");
            }

            if (rateLimit.WindowSeconds < 1)
            {
                errors.Add($"{root}:RateLimit:WindowSeconds must be at least 1.");
            }

            if (rateLimit.QueueLimit < 0)
            {
                errors.Add($"{root}:RateLimit:QueueLimit must not be negative.");
            }

            if (rateLimit.RejectionStatusCode is < 400 or > 599)
            {
                errors.Add($"{root}:RateLimit:RejectionStatusCode must be a 4xx or 5xx status.");
            }
        }

        var forwarded = options.ForwardedHeaders;

        if (forwarded.ForwardLimit < 1)
        {
            errors.Add($"{root}:ForwardedHeaders:ForwardLimit must be at least 1.");
        }

        errors.AddRange(forwarded.KnownProxies
            .Where(proxy => !IPAddress.TryParse(proxy, out _))
            .Select(proxy => $"{root}:ForwardedHeaders:KnownProxies '{proxy}' is not an IP address."));

        errors.AddRange(forwarded.KnownNetworks
            .Where(network => !IPNetwork.TryParse(network, out _))
            .Select(network => $"{root}:ForwardedHeaders:KnownNetworks '{network}' is not a CIDR network."));

        return errors;
    }

    private static IEnumerable<string> ValidateAutoSchema(GraphQlAutoSchemaOptions autoSchema, string key)
    {
        if (autoSchema.DefaultPageSize < 1)
        {
            yield return $"{key}:DefaultPageSize must be at least 1.";
        }

        if (autoSchema.MaxPageSize < autoSchema.DefaultPageSize)
        {
            yield return $"{key}:MaxPageSize must be at least DefaultPageSize ({autoSchema.DefaultPageSize}).";
        }

        if (autoSchema.MaxFilterDepth < 1)
        {
            yield return $"{key}:MaxFilterDepth must be at least 1.";
        }

        if (autoSchema.MaxInValues is < 1 or > 1000)
        {
            yield return $"{key}:MaxInValues must be between 1 and 1000 (Oracle rejects longer IN lists).";
        }

        if (autoSchema.DocumentSampleSize < 1)
        {
            yield return $"{key}:DocumentSampleSize must be at least 1.";
        }

        if (autoSchema.MaxScanItems < 1)
        {
            yield return $"{key}:MaxScanItems must be at least 1.";
        }

        var blankPatterns = autoSchema.ExcludeDatabases
            .Concat(autoSchema.ExcludeSchemas)
            .Concat(autoSchema.ExcludeTables)
            .Concat(autoSchema.ExcludeColumns)
            .Any(string.IsNullOrWhiteSpace);

        if (blankPatterns)
        {
            yield return $"{key}: exclusion patterns must not be blank.";
        }
    }
}