using Domain.Abstractions;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.Configuration;
using Domain.Models.DynamicData;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;

namespace WebApi.GraphQL.AutoSchema;

public sealed partial class AutoSchemaTypeModule(
    IServiceProvider services,
    GraphQlAutoSchemaOptions options,
    IReadOnlyList<string> databaseIds,
    string queryTypeName,
    ILogger<AutoSchemaTypeModule> logger) : TypeModule
{
    public override async ValueTask<IReadOnlyCollection<ITypeSystemMember>> CreateTypesAsync(
        IDescriptorContext context,
        CancellationToken cancellationToken)
    {
        var schemas = new List<DataSourceSchema>();
        var skipped = new Dictionary<string, string>(StringComparer.Ordinal);
        var statuses = new Dictionary<string, DatabaseExposure>(StringComparer.Ordinal);

        foreach (var databaseId in databaseIds)
        {
            if (SchemaExposurePolicy.IsDatabaseExcluded(databaseId, options))
            {
                skipped[databaseId] = "excluded by Api:GraphQlServer:AutoSchema:ExcludeDatabases";
                statuses[databaseId] = DatabaseExposure.Excluded;

                continue;
            }

            var (schema, exposureFailure, failure) = await DescribeAsync(databaseId, cancellationToken).ConfigureAwait(false);

            if (schema is null)
            {
                skipped[databaseId] = failure!;
                statuses[databaseId] = exposureFailure;

                continue;
            }

            schemas.Add(schema);
        }

        var exposure = SchemaExposurePolicy.Expose(
            schemas,
            options,
            [],
            [queryTypeName, .. AutoSchemaScalars.SharedTypeNames]);

        var empty = schemas
            .Select(schema => schema.DatabaseId)
            .Where(databaseId => exposure.Sources.All(source => source.DatabaseId != databaseId));

        foreach (var databaseId in empty)
        {
            skipped[databaseId] = "no exposable tables or collections";
            statuses[databaseId] = DatabaseExposure.NothingToExpose;
        }

        if (exposure.Sources.Count == 0)
        {
            LogNothingExposed(SchemaExposurePolicy.NothingExposedMessage(databaseIds, skipped));
        }

        var report = databaseIds
            .Select(id => new DatabaseExposureStatus(id, statuses.GetValueOrDefault(id, DatabaseExposure.Exposed)))
            .ToList();

        foreach (var note in exposure.Notes)
        {
            LogNote(note);
        }

        foreach (var source in exposure.Sources)
        {
            LogExposed(source.DatabaseId, source.FieldName, source.Sets.Count);
        }

        return new AutoSchemaTypeFactory(options, queryTypeName).Create(exposure, report);
    }

    private async Task<(DataSourceSchema? Schema, DatabaseExposure Exposure, string? Failure)> DescribeAsync(string databaseId, CancellationToken cancellationToken)
    {
        var scope = services.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            var source = scope.ServiceProvider.GetKeyedService<IDynamicDataSource>(databaseId);

            if (source is null)
            {
                LogNoSource(databaseId);

                return (null, DatabaseExposure.NoDataSource, "its engine has no dynamic data source");
            }

            try
            {
                return (await source.DescribeAsync(cancellationToken).ConfigureAwait(false), DatabaseExposure.Exposed, null);
            }
            catch (Exception exception) when (!options.FailOnIntrospectionError && exception is not OperationCanceledException)
            {
                LogIntrospectionFailed(exception, databaseId);

                return (null, DatabaseExposure.Unreachable, $"introspection failed ({exception.GetType().Name}: {exception.Message})");
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{Message}")]
    private partial void LogNothingExposed(string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "GraphQL auto schema: {Note}")]
    private partial void LogNote(string note);

    [LoggerMessage(Level = LogLevel.Information, Message = "GraphQL auto schema exposes database {DatabaseId} as '{FieldName}' with {Count} table(s)/collection(s).")]
    private partial void LogExposed(string databaseId, string fieldName, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "GraphQL auto schema: database {DatabaseId} has no dynamic data source; it is not exposed.")]
    private partial void LogNoSource(string databaseId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "GraphQL auto schema could not introspect database {DatabaseId}; it is left out of the schema.")]
    private partial void LogIntrospectionFailed(Exception exception, string databaseId);
}
