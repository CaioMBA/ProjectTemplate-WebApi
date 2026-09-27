using Domain.Enums;
using Domain.Interfaces.Scheduling;
using Domain.Models.Configuration;
using Hangfire;
using Hangfire.InMemory;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Scheduling.Jobs;
using Shouldly;

namespace UnitTests.Platform;

public sealed class SchedulingTests
{
    [Fact]
    public void DatabaseStorageMustNameAnExistingSupportedDatabase()
    {
        var settings = Enabled(storage: new SchedulingStorageSettings { Type = SchedulingStorageType.Database, DatabaseId = "NOPE" });

        settings.ConnectionErrors().ShouldContain(error => error.Contains("Settings:Scheduling:Storage:DatabaseId 'NOPE'"));

        settings.Databases.Add(new DatabaseSettings { Id = "NOPE", Type = DatabaseType.Sqlite });

        settings.ConnectionErrors().ShouldContain(error => error.Contains("is a Sqlite database, which cannot store jobs") && error.Contains("Postgresql, SqlServer, MongoDb"));

        settings.Databases[0].Type = DatabaseType.Postgresql;

        settings.ConnectionErrors().ShouldBeEmpty();
    }

    [Fact]
    public void AMongoDbEntryIsAcceptedAsJobStorage()
    {
        var settings = Enabled(storage: new SchedulingStorageSettings { Type = SchedulingStorageType.Database, DatabaseId = "JOBS" });
        settings.Databases.Add(new DatabaseSettings { Id = "JOBS", Type = DatabaseType.MongoDb });

        settings.ConnectionErrors().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(DatabaseType.CosmosDb, "unmaintained since 2023")]
    [InlineData(DatabaseType.RavenDb, "RavenDB.Client 3.5")]
    [InlineData(DatabaseType.DynamoDb, "no Hangfire storage exists for DynamoDB")]
    [InlineData(DatabaseType.Mysql, "no Hangfire storage is wired for it")]
    public void UnsupportedEnginesExplainWhy(DatabaseType type, string reason)
    {
        var settings = Enabled(storage: new SchedulingStorageSettings { Type = SchedulingStorageType.Database, DatabaseId = "JOBS" });
        settings.Databases.Add(new DatabaseSettings { Id = "JOBS", Type = type });

        settings.ConnectionErrors().ShouldContain(error =>
            error.Contains("Settings:Scheduling:Storage:DatabaseId 'JOBS'") && error.Contains(reason));
    }

    [Theory]
    [InlineData("not a cron", "UTC", "Cron 'not a cron' is not a valid cron expression")]
    [InlineData("0 3 * * *", "Mars/Olympus", "TimeZone 'Mars/Olympus' is not a known time zone")]
    public void JobOverridesAreValidated(string cron, string timeZone, string expected)
    {
        var settings = Enabled();
        settings.Scheduling.Jobs.Add(new ScheduledJobSettings { Id = "probe", Cron = cron, TimeZone = timeZone });

        settings.ConnectionErrors().ShouldContain(error => error.Contains(expected));
    }

    [Fact]
    public void DuplicateJobIdsAndNoWorkersAreRejected()
    {
        var settings = Enabled();
        settings.Scheduling.Workers = 0;
        settings.Scheduling.Jobs.Add(new ScheduledJobSettings { Id = "a" });
        settings.Scheduling.Jobs.Add(new ScheduledJobSettings { Id = "A" });

        var errors = settings.ConnectionErrors();

        errors.ShouldContain(error => error.Contains("Settings:Scheduling:Workers"));
        errors.ShouldContain(error => error.Contains("Settings:Scheduling:Jobs declares duplicate Ids"));
    }

    [Fact]
    public void DisabledSchedulingIsNotValidated()
    {
        var settings = new AppSettings();
        settings.Scheduling.Workers = 0;

        settings.ConnectionErrors().ShouldBeEmpty();
    }

    [Fact]
    public void TheCatalogRejectsTwoJobsWithTheSameId()
    {
        var services = new ServiceCollection();
        services.AddScoped<IRecurringJob>(_ => new ProbeJob("same", "* * * * *"));
        services.AddScoped<IRecurringJob>(_ => new OtherProbeJob("SAME"));

        using var provider = services.BuildServiceProvider();

        Should.Throw<InvalidOperationException>(() => new RecurringJobCatalog(provider.GetRequiredService<IServiceScopeFactory>()))
            .Message.ShouldContain("'same'");
    }

    [Fact]
    public void TheRegistrarAppliesOverridesDisablesJobsAndRemovesStaleOnes()
    {
        using var storage = new InMemoryStorage();
        var manager = new RecurringJobManager(storage);

        manager.AddOrUpdate("stale", () => Console.WriteLine("gone"), Cron.Daily());

        var settings = Enabled();
        settings.Scheduling.Jobs.Add(new ScheduledJobSettings { Id = "nightly", Cron = "15 4 * * *", TimeZone = "UTC" });
        settings.Scheduling.Jobs.Add(new ScheduledJobSettings { Id = "hourly", Enabled = false });

        var registrar = Registrar(storage, manager, settings, new ProbeJob("nightly", "0 3 * * *"), new OtherProbeJob("hourly"));

        registrar.Apply(settings.Scheduling);

        var jobs = RecurringJobs(storage);

        jobs.Keys.ShouldBe(["nightly"]);
        jobs["nightly"].Cron.ShouldBe("15 4 * * *");

        settings.Scheduling.Jobs.Clear();
        registrar.Apply(settings.Scheduling);

        jobs = RecurringJobs(storage);

        jobs.Keys.ShouldBe(["nightly", "hourly"], ignoreOrder: true);
        jobs["nightly"].Cron.ShouldBe("0 3 * * *");
    }

    private static Dictionary<string, RecurringJobDto> RecurringJobs(JobStorage storage)
    {
        using var connection = storage.GetConnection();

        return connection.GetRecurringJobs().ToDictionary(job => job.Id);
    }

    private static RecurringJobRegistrar Registrar(
        JobStorage storage,
        IRecurringJobManager manager,
        AppSettings settings,
        params IRecurringJob[] jobs)
    {
        var services = new ServiceCollection();

        foreach (var job in jobs)
        {
            services.AddScoped<IRecurringJob>(_ => job);
        }

        var provider = services.BuildServiceProvider();

        return new RecurringJobRegistrar(
            new RecurringJobCatalog(provider.GetRequiredService<IServiceScopeFactory>()),
            manager,
            storage,
            new StaticMonitor(settings),
            new HostingEnvironment { EnvironmentName = Environments.Development },
            NullLogger<RecurringJobRegistrar>.Instance);
    }

    private static AppSettings Enabled(SchedulingStorageSettings? storage = null)
    {
        var settings = new AppSettings();
        settings.Scheduling.Enabled = true;
        settings.Scheduling.Storage = storage ?? new SchedulingStorageSettings();

        return settings;
    }

    private sealed class ProbeJob(string id, string cron) : IRecurringJob
    {
        public string Id => id;

        public string Cron => cron;

        public Task ExecuteAsync(IJobProgress progress, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class OtherProbeJob(string id) : IRecurringJob
    {
        public string Id => id;

        public string Cron => "0 * * * *";

        public Task ExecuteAsync(IJobProgress progress, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StaticMonitor(AppSettings value) : IOptionsMonitor<AppSettings>
    {
        public AppSettings CurrentValue => value;

        public AppSettings Get(string? name) => value;

        public IDisposable? OnChange(Action<AppSettings, string?> listener) => null;
    }
}
