using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Jobs;
using Data.Sql.EntityFrameworkContexts;
using Domain.Entities;
using Domain.Interfaces.Scheduling;
using Domain.Models.Requests.Products;
using Hangfire;
using Hangfire.Storage.Monitoring;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class SchedulingTests(ApiFactory factory)
{
    private static readonly TimeSpan _deadline = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task TriggeringTheOutboxCleanupJobPurgesOldMessagesAndWritesItsLogsToTheDashboardConsole()
    {
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddTransient<IStartupFilter>(_ => new RemoteAddressFilter(IPAddress.Loopback))));

        var client = app.CreateClient();

        var jobId = await AssertCleanupRunsAsync(app.Services);

        var details = await client.GetStringAsync(new Uri($"/hangfire/jobs/details/{jobId}", UriKind.Relative));

        details.ShouldContain("Purged");
        details.ShouldContain("processed outbox messages");
    }

    [Fact]
    public async Task JobsRunAgainstPostgresStorageAndHangfireCreatesItsOwnSchema()
    {
        using var app = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Settings:Scheduling:Storage:Type", "Database");
            builder.UseSetting("Settings:Scheduling:Storage:DatabaseId", ProductsStore.DatabaseId);
            builder.UseSetting("Settings:Scheduling:Storage:Schema", "hangfire_tests");
        });

        _ = app.CreateClient();

        await AssertCleanupRunsAsync(app.Services);

        await using var scope = app.Services.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredKeyedService<AppDbContext>(ProductsStore.DatabaseId);

        var tables = await context.Database
            .SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = 'hangfire_tests'")
            .SingleAsync();

        tables.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task TheSchedulerReportsHealthyWithoutAffectingReadiness()
    {
        var client = factory.CreateClient();

        var health = await client.GetFromJsonAsync<JsonElement>(new Uri("/health", UriKind.Relative));

        health.GetProperty("entries").GetProperty("scheduler").GetProperty("status").GetString().ShouldBe("Healthy");

        var ready = await client.GetStringAsync(new Uri("/ready", UriKind.Relative));

        ready.ShouldNotContain("scheduler");
    }

    [Theory]
    [InlineData("127.0.0.1", HttpStatusCode.OK)]
    [InlineData("203.0.113.7", HttpStatusCode.Unauthorized)]
    public async Task TheDashboardIsServedOnlyToLocalRequests(string remoteAddress, HttpStatusCode expected)
    {
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddTransient<IStartupFilter>(_ => new RemoteAddressFilter(IPAddress.Parse(remoteAddress)))));

        var response = await app.CreateClient().GetAsync(new Uri("/hangfire/recurring", UriKind.Relative));

        response.StatusCode.ShouldBe(expected);

        if (expected == HttpStatusCode.OK)
        {
            (await response.Content.ReadAsStringAsync()).ShouldContain(OutboxCleanupJob.JobId);
        }
    }

    private sealed class RemoteAddressFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = address;
                context.Connection.LocalIpAddress = IPAddress.Loopback;

                return nextMiddleware(context);
            });

            next(app);
        };
    }
    private static async Task<string> AssertCleanupRunsAsync(IServiceProvider services)
    {
        var old = await SeedProcessedMessageAsync(services, DateTime.UtcNow.AddDays(-30));
        var recent = await SeedProcessedMessageAsync(services, DateTime.UtcNow);

        var monitoring = services.GetRequiredService<JobStorage>().GetMonitoringApi();
        var succeededBefore = monitoring.SucceededListCount();

        services.GetRequiredService<IJobScheduler>().Trigger(OutboxCleanupJob.JobId);

        var started = DateTime.UtcNow;

        while (monitoring.SucceededListCount() == succeededBefore)
        {
            if (DateTime.UtcNow - started > _deadline)
            {
                var failed = monitoring.FailedJobs(0, 5).Select(job => job.Value.ExceptionMessage);

                throw new TimeoutException($"The cleanup job did not succeed in time. Failures: {string.Join(" | ", failed)}");
            }

            await Task.Delay(250);
        }

        var succeeded = monitoring.SucceededJobs(0, 10)
            .FirstOrDefault(pair => pair.Value.Job?.Args.Contains(OutboxCleanupJob.JobId) == true);

        succeeded.Key.ShouldNotBeNull();

        await using var scope = services.CreateAsyncScope();

        var outbox = scope.ServiceProvider.GetRequiredKeyedService<AppDbContext>(ProductsStore.DatabaseId).OutboxMessages;

        (await outbox.AnyAsync(message => message.Id == old)).ShouldBeFalse();
        (await outbox.AnyAsync(message => message.Id == recent)).ShouldBeTrue();

        return succeeded.Key;
    }

    private static async Task<Guid> SeedProcessedMessageAsync(IServiceProvider services, DateTime processedOnUtc)
    {
        await using var scope = services.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredKeyedService<AppDbContext>(ProductsStore.DatabaseId);

        var message = new OutboxMessageEntity
        {
            EventType = "probe.cleanup",
            ContentType = "application/json",
            Payload = "{}",
            OccurredOnUtc = processedOnUtc,
            ProcessedOnUtc = processedOnUtc,
        };

        context.OutboxMessages.Add(message);

        await context.SaveChangesAsync();

        return message.Id;
    }
}
