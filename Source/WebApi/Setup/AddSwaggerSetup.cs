using Asp.Versioning.ApiExplorer;
using Domain.Models.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace WebApi.Setup;

public static class SwaggerSetup
{
    public static IServiceCollection AddSwaggerSetup(
        this IServiceCollection services,
        ApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Documentation.Enabled)
        {
            return services;
        }

        services.AddEndpointsApiExplorer();

        services.ConfigureOptions<ConfigureSwaggerOptions>();

        services.AddSwaggerGen(swagger =>
        {
            swagger.CustomSchemaIds(type => type.FullName?.Replace('+', '.'));

            swagger.SupportNonNullableReferenceTypes();

            swagger.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Description = "JWT bearer token. Send the token only, without the 'Bearer ' prefix.",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
            });
        });

        return services;
    }
}

internal sealed class ConfigureSwaggerOptions(
    IApiVersionDescriptionProvider versionProvider,
    IOptions<AppSettings> settings,
    IOptions<ApiOptions> apiOptions) : IConfigureOptions<SwaggerGenOptions>
{
    public void Configure(SwaggerGenOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var appSettings = settings.Value;
        var documentation = apiOptions.Value.Documentation;

        foreach (var description in versionProvider.ApiVersionDescriptions)
        {
            var info = new OpenApiInfo
            {
                Title = appSettings.AppName,
                Version = description.ApiVersion.ToString(),
                Description = description.IsDeprecated
                    ? $"Build {appSettings.AppVersion}. " +
                      $"This API version is deprecated and will be removed in a future release."
                    : $"Build {appSettings.AppVersion}.",
            };

            if (!string.IsNullOrWhiteSpace(documentation.ContactName))
            {
                info.Contact = new OpenApiContact
                {
                    Name = documentation.ContactName,
                    Url = documentation.ContactUrl,
                };
            }

            options.SwaggerDoc(description.GroupName, info);
        }
    }
}
