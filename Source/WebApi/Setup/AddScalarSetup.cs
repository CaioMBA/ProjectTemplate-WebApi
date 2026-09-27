using Asp.Versioning.ApiExplorer;
using Domain.Models.Configuration;
using Scalar.AspNetCore;

namespace WebApi.Setup;

public static class ScalarSetup
{
    public static IServiceCollection AddScalarSetup(this IServiceCollection services, ApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        return services;
    }
}

public static class UseScalarSetup
{
    public static WebApplication UseScalarSetupPipeline(this WebApplication app, ApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Documentation.Enabled || !options.Documentation.Scalar)
        {
            return app;
        }

        var versionProvider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();

        app.MapScalarApiReference(options.Documentation.ScalarRoute, scalar =>
        {
            scalar.WithTitle("API Reference");

            var groupNames = versionProvider.ApiVersionDescriptions
                .Select(description => description.GroupName)
                .Reverse();

            foreach (var groupName in groupNames)
            {
                scalar.AddDocument(
                    groupName,
                    groupName.ToUpperInvariant(),
                    $"/swagger/{groupName}/swagger.json");
            }
        });

        return app;
    }
}
