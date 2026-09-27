using Asp.Versioning.ApiExplorer;
using Domain.Models.Configuration;

namespace WebApi.Setup;

public static class UseSwaggerSetup
{
    public static WebApplication UseSwaggerSetupPipeline(this WebApplication app, ApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Documentation.Enabled)
        {
            return app;
        }

        app.UseSwagger();

        if (!options.Documentation.SwaggerUi)
        {
            return app;
        }

        var versionProvider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();

        app.UseSwaggerUI(ui =>
        {
            ui.RoutePrefix = options.Documentation.SwaggerUiRoute;

            var groupNames = versionProvider.ApiVersionDescriptions
                .Select(description => description.GroupName)
                .Reverse();

            foreach (var groupName in groupNames)
            {
                ui.SwaggerEndpoint($"/swagger/{groupName}/swagger.json", groupName.ToUpperInvariant());
            }

            ui.DisplayRequestDuration();
            ui.EnableTryItOutByDefault();
        });

        return app;
    }
}
