using Domain.Models.Configuration;

namespace WebApi.Setup;

public static class CorsSetup
{
    public static IServiceCollection AddCorsSetup(this IServiceCollection services, ApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var cors = options.Cors;

        if (!cors.Enabled)
        {
            return services;
        }

        services.AddCors(setup => setup.AddPolicy(cors.PolicyName, policy =>
        {
            if (cors.AllowedOrigins.Count > 0)
            {
                policy.WithOrigins([.. cors.AllowedOrigins]);

                policy.SetIsOriginAllowedToAllowWildcardSubdomains();
            }
            else
            {
                policy.WithOrigins();
            }

            if (cors.AllowedMethods.Count > 0)
            {
                policy.WithMethods([.. cors.AllowedMethods]);
            }
            else
            {
                policy.AllowAnyMethod();
            }

            if (cors.AllowedHeaders.Count > 0)
            {
                policy.WithHeaders([.. cors.AllowedHeaders]);
            }
            else
            {
                policy.AllowAnyHeader();
            }

            if (cors.AllowCredentials && cors.AllowedOrigins.Count > 0)
            {
                policy.AllowCredentials();
            }

            policy.SetPreflightMaxAge(TimeSpan.FromSeconds(cors.PreflightMaxAgeSeconds));
        }));

        return services;
    }

    public static WebApplication UseCorsSetupPipeline(this WebApplication app, ApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Cors.Enabled)
        {
            app.UseCors(options.Cors.PolicyName);
        }

        return app;
    }
}
