using System.Runtime.CompilerServices;
using Asp.Versioning;
using Asp.Versioning.Conventions;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using WebApi.Controllers;

namespace WebApi.Setup;

public static class VersioningSetup
{
    public static IServiceCollection AddVersioningSetup(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddApiVersioning(options =>
            {
                options.ReportApiVersions = true;

                options.AssumeDefaultVersionWhenUnspecified = false;

                options.DefaultApiVersion = new ApiVersion(1, 0);

                options.ApiVersionReader = new UrlSegmentApiVersionReader();
            })
            .AddMvc(options => options.Conventions.Add(new VersionByFolderConvention()))
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            });

        return services;
    }
}

internal sealed class VersionByFolderConvention : IControllerConvention
{
    private readonly NamespaceParser _parser = NamespaceParser.Default;

    public bool Apply(IControllerConventionBuilder builder, ControllerModel controller)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(controller);

        var type = controller.ControllerType.AsType();
        var versions = _parser.Parse(type);

        if (versions.Count == 0)
        {
            throw new InvalidOperationException(
                $"Controller {type.FullName} is not in a version folder. Put it under Controllers/V<n>/ "
                + "(namespace WebApi.Controllers.V<n>), e.g. Controllers/V1/.");
        }

        var deprecated = IsDeprecated(type);

        foreach (var version in versions)
        {
            if (deprecated)
            {
                builder.HasDeprecatedApiVersion(version);
            }
            else
            {
                builder.HasApiVersion(version);
            }
        }

        return true;
    }

    private static bool IsDeprecated(Type controllerType)
    {
        if (!typeof(ApiControllerBase).IsAssignableFrom(controllerType) || controllerType.IsAbstract)
        {
            return false;
        }

        var controller = (ApiControllerBase)RuntimeHelpers.GetUninitializedObject(controllerType);

        return controller.IsDeprecated;
    }
}
