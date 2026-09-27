using Domain.Models.Configuration;
using HotChocolate;
using HotChocolate.Execution.Configuration;
using WebApi.GraphQL.AutoSchema;

namespace WebApi.Setup;

public static class GraphQlSetup
{
    public const string QueryTypeName = "Query";

    public static IServiceCollection AddGraphQlSetup(
        this IServiceCollection services,
        ApiOptions options,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settings);

        var graphQl = options.GraphQlServer;

        if (!graphQl.Enabled)
        {
            return services;
        }

        var builder = services
            .AddGraphQLServer()

            .AddQueryType(descriptor => descriptor.Name(QueryTypeName))

            .AddMaxExecutionDepthRule(graphQl.MaxExecutionDepth);

        AddAutoSchema(services, builder, graphQl.AutoSchema, settings);

        if (!graphQl.IncludeExceptionDetails)
        {
            builder.ModifyRequestOptions(request => request.IncludeExceptionDetails = false);
        }
        else
        {
            builder.ModifyRequestOptions(request => request.IncludeExceptionDetails = true);
        }

        return services;
    }

    private static void AddAutoSchema(
        IServiceCollection services,
        IRequestExecutorBuilder builder,
        GraphQlAutoSchemaOptions autoSchema,
        AppSettings settings)
    {
        var databaseIds = settings.Databases.Select(database => database.Id).ToList();

        services.AddDataLoader<AutoSchemaRowLoader>();

        builder.AddTypeModule(provider =>
        {
            var root = provider.GetService<IRootServiceProviderAccessor>()?.ServiceProvider ?? provider;

            return new AutoSchemaTypeModule(
                root,
                autoSchema,
                databaseIds,
                QueryTypeName,
                root.GetRequiredService<ILogger<AutoSchemaTypeModule>>());
        });
    }

    public static WebApplication UseGraphQlSetupPipeline(this WebApplication app, ApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(options);

        var graphQl = options.GraphQlServer;

        if (!graphQl.Enabled)
        {
            return app;
        }

        app.MapGraphQL(graphQl.Route)
            .WithOptions(options =>

                options.Tool.Enable = graphQl.EnableTool);

        return app;
    }
}
