using System.Text.RegularExpressions;
using Domain.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace WebApi.Setup;

public static class ControllersSetup
{
    public static IServiceCollection AddControllersSetup(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddControllers(options =>
            {
                options.ReturnHttpNotAcceptable = true;
                options.Conventions.Add(new RouteTokenTransformerConvention(new KebabCaseRouteTransformer()));
            })
            .AddJsonOptions(options =>
            {
                var shared = JsonDefaults.Standard;

                options.JsonSerializerOptions.PropertyNamingPolicy = shared.PropertyNamingPolicy;
                options.JsonSerializerOptions.DefaultIgnoreCondition = shared.DefaultIgnoreCondition;
                options.JsonSerializerOptions.ReadCommentHandling = shared.ReadCommentHandling;
                options.JsonSerializerOptions.AllowTrailingCommas = shared.AllowTrailingCommas;
                options.JsonSerializerOptions.NumberHandling = shared.NumberHandling;

                foreach (var converter in shared.Converters)
                {
                    options.JsonSerializerOptions.Converters.Add(converter);
                }
            });

        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.SuppressModelStateInvalidFilter = true;
        });

        return services;
    }
}

internal sealed partial class KebabCaseRouteTransformer : IOutboundParameterTransformer
{
    public string? TransformOutbound(object? value) =>
        value is null ? null : WordBoundary().Replace(value.ToString()!, "$1-$2").ToLowerInvariant();

    [GeneratedRegex("([a-z0-9])([A-Z])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WordBoundary();
}
