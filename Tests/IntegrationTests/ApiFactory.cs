using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("webapi_template_tests")
        .WithUsername("test")
        .WithPassword("test")

        .WithCleanUp(true)
        .Build();

    private readonly List<string> _environmentKeys = [];

    private readonly Dictionary<string, Task<WebApplicationFactory<Program>>> _derived = new(StringComparer.Ordinal);

    private readonly Lock _derivedLock = new();

    public string PostgresConnectionString => _postgres.GetConnectionString();

    public Task<WebApplicationFactory<Program>> SharedDerivedAsync(
        string key,
        Func<Task<WebApplicationFactory<Program>>> create)
    {
        ArgumentNullException.ThrowIfNull(create);

        lock (_derivedLock)
        {
            if (!_derived.TryGetValue(key, out var host))
            {
                host = create();
                _derived[key] = host;
            }

            return host;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Development");
    }

    private void PublishConfiguration()
    {
        var port = _postgres.GetMappedPublicPort(5432);

        var settings = new Dictionary<string, string?>
        {
                ["Settings:Databases:0:Id"] = "DEFAULT",
                ["Settings:Databases:0:Type"] = "Postgresql",
                ["Settings:Databases:0:Host"] = _postgres.Hostname,
                ["Settings:Databases:0:Port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Settings:Databases:0:Database"] = "webapi_template_tests",
                ["Settings:Databases:0:Username"] = "test",
                ["Settings:Databases:0:Password"] = "test",
                ["Settings:Databases:0:Sql:MigrateOnStartup"] = "true",
                ["Settings:Databases:0:Sql:Outbox:Enabled"] = "false",

                ["Api:Documentation:Enabled"] = "true",
                ["Api:GraphQlServer:Enabled"] = "true",

                ["Api:RateLimit:Enabled"] = "false",

                ["Observability:HealthChecks:ExposeDetails"] = "true",
                ["Observability:HealthChecks:UiEnabled"] = "true",
                ["Settings:Scheduling:Enabled"] = "true",
                ["Settings:Scheduling:Dashboard:Enabled"] = "true",

            ["Observability:Enabled"] = "false",
        };

        foreach (var (key, value) in settings)
        {
            var environmentKey = key.Replace(":", "__", StringComparison.Ordinal);

            Environment.SetEnvironmentVariable(environmentKey, value);
            _environmentKeys.Add(environmentKey);
        }
    }

    public Task PauseDatabaseAsync() => _postgres.PauseAsync();

    public Task ResumeDatabaseAsync() => _postgres.UnpauseAsync();

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _postgres.StartAsync().ConfigureAwait(false);

        PublishConfiguration();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        foreach (var host in _derived.Values)
        {
            await (await host.ConfigureAwait(false)).DisposeAsync().ConfigureAwait(false);
        }

        await base.DisposeAsync().ConfigureAwait(false);
        await _postgres.DisposeAsync().ConfigureAwait(false);

        foreach (var key in _environmentKeys)
        {
            Environment.SetEnvironmentVariable(key, null);
        }

        _environmentKeys.Clear();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "Api";
}
