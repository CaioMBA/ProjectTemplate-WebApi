using Domain.Enums;

namespace Domain.Models.Configuration;

public sealed class AppSettings
{
    public const string SectionName = "Settings";

    public string AppName { get; set; } = "WebApi Template";

    public string AppVersion { get; set; } = "1.0.0";

    public string PathBase { get; set; } = string.Empty;

    public List<DatabaseSettings> Databases { get; set; } = [];

    public List<CacheSettings> Caches { get; set; } = [];

    public List<BrokerSettings> Brokers { get; set; } = [];

    public List<ApiSettings> Apis { get; set; } = [];

    public SchedulingSettings Scheduling { get; set; } = new();

    public DatabaseSettings GetDatabase(string id) =>
        Get(Databases, nameof(Databases), connection => connection.Id, id);

    public DatabaseSettings GetDatabase(string id, DatabaseFamily expected)
    {
        var match = GetDatabase(id);

        var actual = match.Type.Family();

        return actual == expected
            ? match
            : throw new InvalidOperationException(
                $"{SectionName}:{nameof(Databases)} entry '{match.Id}' has Type '{match.Type}', "
                + $"which is a {actual} database, but a {expected} database is required here.");
    }

    public CacheSettings GetCache(string id) =>
        Get(Caches, nameof(Caches), connection => connection.Id, id);

    public CacheSettings? FindCache(string id) =>
        Find(Caches, connection => connection.Id, id);

    public BrokerSettings GetBroker(string id) =>
        Get(Brokers, nameof(Brokers), connection => connection.Id, id);

    public void ValidateConnections()
    {
        var errors = ConnectionErrors();

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }
    }

    public IReadOnlyList<string> ConnectionErrors()
    {
        var errors = new List<string>();

        errors.AddRange(IdErrors(Databases, nameof(Databases), connection => connection.Id));
        errors.AddRange(IdErrors(Caches, nameof(Caches), connection => connection.Id));
        errors.AddRange(IdErrors(Brokers, nameof(Brokers), connection => connection.Id));
        errors.AddRange(IdErrors(Apis, nameof(Apis), api => api.Id));

        foreach (var api in Apis.Where(api => !string.IsNullOrWhiteSpace(api.Id)))
        {
            errors.AddRange(IdErrors(api.Endpoints, $"{nameof(Apis)}[Id={api.Id}]:{nameof(ApiSettings.Endpoints)}", endpoint => endpoint.Id));
        }

        errors.AddRange(Caches
            .Where(connection => connection.DefaultTtlMinutes <= 0)
            .Select(connection =>
                $"{connection.KeyOf(nameof(CacheSettings.DefaultTtlMinutes))} must be greater than zero; "
                + $"it is {connection.DefaultTtlMinutes}."));

        foreach (var database in Databases.Where(database => database.Type.Family() == DatabaseFamily.Relational))
        {
            errors.AddRange(SqlErrors(database));
        }

        errors.AddRange(SchedulingErrors());

        return errors;
    }

    public ApiSettings GetApi(string apiId) =>
        Get(Apis, nameof(Apis), api => api.Id, apiId);
    public ApiEndpointSettings GetApiEndpoint(string apiId, string endpointId)
    {
        var api = GetApi(apiId);

        var match = api.Endpoints.Find(candidate =>
            string.Equals(candidate.Id, endpointId, StringComparison.OrdinalIgnoreCase));

        return match
            ?? throw new InvalidOperationException(
                $"API '{apiId}' has no endpoint configured with Id '{endpointId}'.");
    }

    private IEnumerable<string> SqlErrors(DatabaseSettings database)
    {
        var outbox = database.Sql.Outbox;
        var pagedCache = database.Sql.PagedCache;

        if (outbox.PollIntervalSeconds < 1)
        {
            yield return $"{database.KeyOf("Sql:Outbox:PollIntervalSeconds")} must be at least 1; it is {outbox.PollIntervalSeconds}.";
        }

        if (outbox.RetentionDays < 1)
        {
            yield return $"{database.KeyOf("Sql:Outbox:RetentionDays")} must be at least 1; it is {outbox.RetentionDays}.";
        }

        if (outbox.BatchSize < 1)
        {
            yield return $"{database.KeyOf("Sql:Outbox:BatchSize")} must be at least 1; it is {outbox.BatchSize}.";
        }

        if (outbox.Enabled && string.IsNullOrWhiteSpace(outbox.BrokerId))
        {
            yield return $"{database.KeyOf("Sql:Outbox:BrokerId")} is required when the outbox is enabled.";
        }
        else if (!string.IsNullOrWhiteSpace(outbox.BrokerId) && Find(Brokers, broker => broker.Id, outbox.BrokerId) is null)
        {
            yield return $"{database.KeyOf("Sql:Outbox:BrokerId")} is '{outbox.BrokerId}', which is not an Id in {SectionName}:{nameof(Brokers)}.";
        }

        if (pagedCache.MaxCachedPages < 1)
        {
            yield return $"{database.KeyOf("Sql:PagedCache:MaxCachedPages")} must be at least 1; it is {pagedCache.MaxCachedPages}.";
        }

        if (pagedCache.DefaultTtlMinutes <= 0)
        {
            yield return $"{database.KeyOf("Sql:PagedCache:DefaultTtlMinutes")} must be greater than zero; it is {pagedCache.DefaultTtlMinutes}.";
        }

        if (!string.IsNullOrWhiteSpace(pagedCache.CacheId) && Find(Caches, cache => cache.Id, pagedCache.CacheId) is null)
        {
            yield return $"{database.KeyOf("Sql:PagedCache:CacheId")} is '{pagedCache.CacheId}', which is not an Id in {SectionName}:{nameof(Caches)}.";
        }
    }

    private IEnumerable<string> SchedulingErrors()
    {
        const string key = SectionName + ":" + nameof(Scheduling);

        var scheduling = Scheduling;

        if (!scheduling.Enabled)
        {
            yield break;
        }

        if (scheduling.Workers < 1)
        {
            yield return $"{key}:Workers must be at least 1; it is {scheduling.Workers}.";
        }

        if (scheduling.RetryAttempts < 0)
        {
            yield return $"{key}:RetryAttempts must not be negative.";
        }

        if (scheduling.Queues.Count == 0 || scheduling.Queues.Any(string.IsNullOrWhiteSpace))
        {
            yield return $"{key}:Queues needs at least one non-blank queue name.";
        }

        if (scheduling.Dashboard.Enabled && !scheduling.Dashboard.Path.StartsWith('/'))
        {
            yield return $"{key}:Dashboard:Path '{scheduling.Dashboard.Path}' must start with '/'.";
        }

        if (scheduling.Storage.Type == SchedulingStorageType.Database)
        {
            var databaseId = scheduling.Storage.DatabaseId;
            var database = string.IsNullOrWhiteSpace(databaseId) ? null : Find(Databases, entry => entry.Id, databaseId);

            if (database is null)
            {
                yield return $"{key}:Storage:DatabaseId '{databaseId}' must name an Id in {SectionName}:{nameof(Databases)} when Storage:Type is Database.";
            }
            else if (!SupportedSchedulingEngines.ContainsKey(database.Type))
            {
                var reason = _unsupportedSchedulingEngines.TryGetValue(database.Type, out var why)
                    ? why
                    : "no Hangfire storage is wired for it";

                yield return $"{key}:Storage:DatabaseId '{databaseId}' is a {database.Type} database, which cannot store jobs: {reason}. "
                    + $"Job storage supports {string.Join(", ", SupportedSchedulingEngines.Keys)}; use one of those or Storage:Type Memory.";
            }
        }

        foreach (var error in IdErrors(scheduling.Jobs, $"{nameof(Scheduling)}:{nameof(SchedulingSettings.Jobs)}", job => job.Id))
        {
            yield return error;
        }

        foreach (var job in scheduling.Jobs.Where(job => !string.IsNullOrWhiteSpace(job.Id)))
        {
            if (!string.IsNullOrWhiteSpace(job.Cron)
                && NCrontab.CrontabSchedule.TryParse(job.Cron) is null
                && NCrontab.CrontabSchedule.TryParse(job.Cron, new NCrontab.CrontabSchedule.ParseOptions { IncludingSeconds = true }) is null)
            {
                yield return $"{key}:Jobs[Id={job.Id}]:Cron '{job.Cron}' is not a valid cron expression.";
            }

            if (!TimeZoneInfo.TryFindSystemTimeZoneById(job.TimeZone, out _))
            {
                yield return $"{key}:Jobs[Id={job.Id}]:TimeZone '{job.TimeZone}' is not a known time zone.";
            }
        }
    }

    public static readonly IReadOnlyDictionary<DatabaseType, string> SupportedSchedulingEngines =
        new Dictionary<DatabaseType, string>
        {
            [DatabaseType.Postgresql] = "Hangfire.PostgreSql",
            [DatabaseType.SqlServer] = "Hangfire.SqlServer",
            [DatabaseType.MongoDb] = "Hangfire.Mongo",
        };

    private static readonly Dictionary<DatabaseType, string> _unsupportedSchedulingEngines = new()
    {
        [DatabaseType.CosmosDb] = "Hangfire.AzureCosmosDB is unmaintained since 2023 and builds its own Cosmos client",
        [DatabaseType.RavenDb] = "Hangfire.Raven targets RavenDB.Client 3.5 and cannot run beside the 7.x client",
        [DatabaseType.DynamoDb] = "no Hangfire storage exists for DynamoDB",
    };

    private static T? Find<T>(List<T> entries, Func<T, string> idOf, string id)
        where T : class =>
        entries.Find(entry => string.Equals(idOf(entry), id, StringComparison.OrdinalIgnoreCase));

    private static T Get<T>(List<T> entries, string listName, Func<T, string> idOf, string id)
        where T : class =>
        Find(entries, idOf, id)
        ?? throw new InvalidOperationException(
            $"No entry in {SectionName}:{listName} has Id '{id}'. "
            + $"Configured ids: {(entries.Count == 0 ? "none" : string.Join(", ", entries.Select(idOf)))}.");

    private static IEnumerable<string> IdErrors<T>(List<T> entries, string listName, Func<T, string> idOf)
    {
        var blank = entries.FindIndex(entry => string.IsNullOrWhiteSpace(idOf(entry)));

        if (blank >= 0)
        {
            yield return $"{SectionName}:{listName}:{blank}:Id is empty. Every {listName} entry needs an Id.";

            yield break;
        }

        var duplicates = entries
            .GroupBy(idOf, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            yield return $"{SectionName}:{listName} declares duplicate Ids (case-insensitive): "
                + $"{string.Join(", ", duplicates)}.";
        }
    }
}