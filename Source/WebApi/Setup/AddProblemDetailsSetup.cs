using WebApi.Handlers;

namespace WebApi.Setup;

public static class ProblemDetailsSetup
{
    public static IServiceCollection AddProblemDetailsSetup(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
                context.ProblemDetails.Instance ??=
                    $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";
            });

        services.AddExceptionHandler<GlobalExceptionHandler>();

        return services;
    }
}
