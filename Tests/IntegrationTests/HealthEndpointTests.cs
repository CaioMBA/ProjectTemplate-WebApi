using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Abstractions;
using Domain.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class HealthEndpointTests(ApiFactory factory)
{
    [Fact]
    public async Task ReadyChecksTheDatabaseAndIsHealthyWhenItAnswers()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/ready", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        payload.GetProperty("status").GetString().ShouldBe("Healthy");
        (await response.Content.ReadAsStringAsync()).ShouldContain("db:DEFAULT");
    }

    [Theory]
    [InlineData(DependencyKind.Broker)]
    [InlineData(DependencyKind.DocumentDatabase)]
    public async Task AFailingDatabaseOrBrokerTakesTheServiceOutOfReadinessButNotOutOfLiveness(DependencyKind kind)
    {
        using var app = WithFailing(kind);
        var client = app.CreateClient();

        (await client.GetAsync(new Uri("/ready", UriKind.Relative))).StatusCode
            .ShouldBe(HttpStatusCode.ServiceUnavailable);

        (await client.GetAsync(new Uri("/health", UriKind.Relative))).StatusCode
            .ShouldBe(HttpStatusCode.ServiceUnavailable);

        (await client.GetAsync(new Uri("/live", UriKind.Relative))).StatusCode
            .ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(DependencyKind.Cache)]
    [InlineData(DependencyKind.Api)]
    public async Task AFailingCacheOrApiOnlyDegradesHealthAndLeavesReadinessAlone(DependencyKind kind)
    {
        using var app = WithFailing(kind);
        var client = app.CreateClient();

        (await client.GetAsync(new Uri("/ready", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var health = await client.GetAsync(new Uri("/health", UriKind.Relative));

        health.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await health.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString()
            .ShouldBe("Degraded");
    }

    [Fact]
    public async Task AHungDatabaseTimesOutAndFailsReadinessWhileLivenessStaysUp()
    {
        var client = factory.CreateClient();

        await factory.PauseDatabaseAsync();

        try
        {
            var ready = await client.GetAsync(new Uri("/ready", UriKind.Relative));

            ready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

            (await client.GetAsync(new Uri("/live", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            await factory.ResumeDatabaseAsync();
        }

        (await client.GetAsync(new Uri("/ready", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheDashboardPollsItsOwnEndpointAndHealthKeepsItsContainerContract()
    {
        var client = factory.CreateClient();

        var ui = await client.GetAsync(new Uri("/health-ui", UriKind.Relative));

        ui.StatusCode.ShouldBe(HttpStatusCode.OK);
        ui.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");

        var uiApi = await client.GetFromJsonAsync<JsonElement>(new Uri("/health-ui-api", UriKind.Relative));

        uiApi.GetProperty("status").GetString().ShouldBe("Healthy");
        uiApi.TryGetProperty("totalDuration", out _).ShouldBeTrue();
        uiApi.GetProperty("entries").TryGetProperty("db:DEFAULT", out _).ShouldBeTrue();

        var health = await client.GetFromJsonAsync<JsonElement>(new Uri("/health", UriKind.Relative));

        health.GetProperty("status").GetString().ShouldBe("Healthy");
    }

    private WebApplicationFactory<Program> WithFailing(DependencyKind kind) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHealthChecks().Add(HealthCheckPolicy.Registration(
                kind,
                $"stub:{kind}",
                _ => new FailingHealthCheck()))));

    private sealed class FailingHealthCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HealthCheckResult(context.Registration.FailureStatus, "Stubbed outage."));
    }
}
