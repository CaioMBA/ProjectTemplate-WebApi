using Application.Jobs;
using Domain.Interfaces.Scheduling;
using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class MongoSchedulingTests(ApiFactory factory) : IAsyncLifetime
{
    private const string DatabaseName = "webapi_jobs_tests";

    private static readonly TimeSpan _deadline = TimeSpan.FromSeconds(30);

    private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:8.0").Build();

    public Task InitializeAsync() => _mongo.StartAsync();

    public Task DisposeAsync() => _mongo.DisposeAsync().AsTask();

    [Fact]
    public async Task JobsRunOnAStandaloneMongoAndSurviveARestart()
    {
        using (var first = WithMongoStorage())
        {
            _ = first.CreateClient();

            var jobId = await TriggerAndWaitAsync(first.Services);

            jobId.ShouldNotBeNullOrWhiteSpace();
        }

        var collections = await (await new MongoClient(_mongo.GetConnectionString())
                .GetDatabase(DatabaseName)
                .ListCollectionNamesAsync())
            .ToListAsync();

        collections.ShouldContain(name => name.StartsWith("hangfire.", StringComparison.Ordinal));
        collections.ShouldContain("hangfire.jobGraph");

        using var second = WithMongoStorage();
        _ = second.CreateClient();

        var monitoring = second.Services.GetRequiredService<JobStorage>().GetMonitoringApi();

        monitoring.SucceededListCount().ShouldBeGreaterThanOrEqualTo(1);

        using var connection = second.Services.GetRequiredService<JobStorage>().GetConnection();

        connection.GetRecurringJobs().Select(job => job.Id).ShouldContain(OutboxCleanupJob.JobId);
    }

    private WebApplicationFactory<Program> WithMongoStorage() =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Settings:Databases:1:Id", "JOBS");
            builder.UseSetting("Settings:Databases:1:Type", "MongoDb");
            builder.UseSetting("Settings:Databases:1:ConnectionString", _mongo.GetConnectionString());
            builder.UseSetting("Settings:Databases:1:Database", DatabaseName);
            builder.UseSetting("Settings:Scheduling:Storage:Type", "Database");
            builder.UseSetting("Settings:Scheduling:Storage:DatabaseId", "JOBS");
        });

    private static async Task<string> TriggerAndWaitAsync(IServiceProvider services)
    {
        var monitoring = services.GetRequiredService<JobStorage>().GetMonitoringApi();
        var before = monitoring.SucceededListCount();

        services.GetRequiredService<IJobScheduler>().Trigger(OutboxCleanupJob.JobId);

        var started = DateTime.UtcNow;

        while (monitoring.SucceededListCount() == before)
        {
            if (DateTime.UtcNow - started > _deadline)
            {
                var failed = monitoring.FailedJobs(0, 5).Select(job => job.Value.ExceptionMessage);

                throw new TimeoutException($"The job did not succeed on MongoDB storage. Failures: {string.Join(" | ", failed)}");
            }

            await Task.Delay(250);
        }

        return monitoring.SucceededJobs(0, 10)
            .First(pair => pair.Value.Job?.Args.Contains(OutboxCleanupJob.JobId) == true)
            .Key;
    }
}
