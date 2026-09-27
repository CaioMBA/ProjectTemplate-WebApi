using Domain.Abstractions;
using Domain.Enums;
using Domain.Models.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Observability.Setup;
using Shouldly;

namespace UnitTests.Platform;

public sealed class HealthCheckPolicyTests
{
    [Fact]
    public void EveryDependencyKindHasExactlyOneRule() =>
        Enum.GetValues<DependencyKind>().ShouldAllBe(kind => HealthCheckPolicy.For(kind) != null);

    [Theory]
    [InlineData(DependencyKind.Self, true, HealthStatus.Unhealthy)]
    [InlineData(DependencyKind.Database, true, HealthStatus.Unhealthy)]
    [InlineData(DependencyKind.DocumentDatabase, true, HealthStatus.Unhealthy)]
    [InlineData(DependencyKind.Broker, true, HealthStatus.Unhealthy)]
    [InlineData(DependencyKind.Cache, false, HealthStatus.Degraded)]
    [InlineData(DependencyKind.Api, false, HealthStatus.Degraded)]
    [InlineData(DependencyKind.Scheduler, false, HealthStatus.Degraded)]
    public void DatabasesAndBrokersGateReadinessWhileCachesAndApisOnlyDegrade(
        DependencyKind kind,
        bool ready,
        HealthStatus failureStatus)
    {
        var rule = HealthCheckPolicy.For(kind);

        rule.IsReady.ShouldBe(ready);
        rule.FailureStatus.ShouldBe(failureStatus);
        rule.Tags.Contains(HealthCheckTags.Critical).ShouldBe(ready && kind != DependencyKind.Self);
    }

    [Fact]
    public void OnlyTheProcessItselfFeedsLiveness() =>
        Enum.GetValues<DependencyKind>()
            .Where(kind => HealthCheckPolicy.For(kind).Tags.Contains(HealthCheckTags.Self))
            .ShouldBe([DependencyKind.Self]);

    [Fact]
    public void EveryRegistrationGetsTheConfiguredTimeout()
    {
        var options = Options(new HealthCheckOptionsModel { TimeoutSeconds = 3 });

        options.Registrations.ShouldNotBeEmpty();
        options.Registrations.ShouldAllBe(registration => registration.Timeout == TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void AnExplicitTimeoutOnARegistrationIsKept()
    {
        var registration = HealthCheckPolicy.Registration(DependencyKind.Cache, "cache:X", _ => null!);
        registration.Timeout = TimeSpan.FromSeconds(1);

        var serviceOptions = new HealthCheckServiceOptions();
        serviceOptions.Registrations.Add(registration);

        HealthChecksSetup.ApplyTimeoutsAndGuard(serviceOptions, TimeSpan.FromSeconds(9));

        registration.Timeout.ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ADependencyCheckCarryingTheSelfTagIsRejected()
    {
        var serviceOptions = new HealthCheckServiceOptions();
        serviceOptions.Registrations.Add(new HealthCheckRegistration(
            "db:SNEAKY",
            _ => null!,
            HealthStatus.Unhealthy,
            [HealthCheckTags.Self, HealthCheckTags.Database]));

        Should.Throw<InvalidOperationException>(() =>
                HealthChecksSetup.ApplyTimeoutsAndGuard(serviceOptions, TimeSpan.FromSeconds(5)))
            .Message.ShouldContain("db:SNEAKY");
    }

    [Fact]
    public void ApiChecksAreRegisteredWithTheApiRule()
    {
        var settings = new AppSettings
        {
            Apis =
            [
                new ApiSettings { Id = "PARTNER", BaseAddress = new Uri("https://partner.test/"), HealthEndpoint = "/health" },
            ],
        };

        var registrations = Options(new HealthCheckOptionsModel(), settings).Registrations.ToDictionary(r => r.Name);

        var rule = HealthCheckPolicy.For(DependencyKind.Api);

        registrations["api:PARTNER"].FailureStatus.ShouldBe(rule.FailureStatus);
        registrations["api:PARTNER"].Tags.ShouldBe(rule.Tags, ignoreOrder: true);
        registrations["self"].Tags.ShouldBe(HealthCheckPolicy.For(DependencyKind.Self).Tags, ignoreOrder: true);
    }

    private static HealthCheckServiceOptions Options(HealthCheckOptionsModel options, AppSettings? settings = null)
    {
        var services = new ServiceCollection();

        services.AddHealthChecksSetup(settings ?? new AppSettings(), options);

        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
    }
}
