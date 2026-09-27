using CrossCutting.Configuration;
using CrossCutting.Setup;
using Domain.Abstractions;
using Domain.Enums;
using Domain.Interfaces.Platform;
using Domain.Models.Configuration;
using Domain.Setup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using UnitTests.Modules;
using UnitTests.TestSupport;

namespace UnitTests.Platform;

public sealed class StartupSettingsTests
{
    private static readonly Dictionary<string, string> _otel = new()
    {
        [ObservabilityEnvironment.ServiceName] = "orders-api",
        [ObservabilityEnvironment.OtlpEndpoint] = "http://collector:4317",
        [ObservabilityEnvironment.HealthCheckPath] = "/healthz",
    };

    [Fact]
    public void EnvironmentOverridesReachBothTheStartupCopyAndTheOptionsPipeline()
    {
        var configuration = Configuration([]);
        var environment = new FakeEnvironmentAccessor(_otel);

        var startup = TestStartup.From(configuration, environment);

        startup.Observability.ServiceName.ShouldBe("orders-api");
        startup.Observability.OtlpEndpoint.ShouldBe(new Uri("http://collector:4317"));
        startup.Observability.HealthChecks.Path.ShouldBe("/healthz");
        startup.Observability.DeploymentEnvironment.ShouldBe(TestStartup.EnvironmentName);

        using var provider = Container(configuration, startup, environment);

        var options = provider.GetRequiredService<IOptions<ObservabilityOptions>>().Value;

        options.ServiceName.ShouldBe(startup.Observability.ServiceName);
        options.OtlpEndpoint.ShouldBe(startup.Observability.OtlpEndpoint);
        options.HealthChecks.Path.ShouldBe(startup.Observability.HealthChecks.Path);
        options.DeploymentEnvironment.ShouldBe(startup.Observability.DeploymentEnvironment);
    }

    [Theory]
    [InlineData("Observability:HealthChecks:TimeoutSeconds", "0", "Observability:HealthChecks:TimeoutSeconds")]
    [InlineData("Observability:TraceSamplingRatio", "1.5", "Observability:TraceSamplingRatio")]
    [InlineData("Observability:OtlpProtocol", "udp", "Observability:OtlpProtocol")]
    [InlineData("Observability:HealthChecks:ReadinessPath", "/live", "LivenessPath and ReadinessPath")]
    [InlineData("Api:RateLimit:PermitLimit", "0", "Api:RateLimit:PermitLimit")]
    [InlineData("Api:ForwardedHeaders:KnownNetworks:0", "not-a-network", "Api:ForwardedHeaders:KnownNetworks")]
    [InlineData("Api:GraphQlServer:MaxExecutionDepth", "0", "Api:GraphQlServer:MaxExecutionDepth")]
    public void AnInvalidValueFailsStartupNamingItsKey(string key, string value, string expected)
    {
        var configuration = Configuration(new() { [key] = value });

        Should.Throw<OptionsValidationException>(() => TestStartup.From(configuration))
            .Message.ShouldContain(expected);
    }

    [Fact]
    public void TheOptionsPipelineRejectsTheSameInvalidValues()
    {
        var configuration = Configuration(new() { ["Api:Cors:AllowCredentials"] = "true", ["Api:Cors:AllowedOrigins:0"] = "*" });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddServicesSetup(configuration);
        services.AddDomainSetup(configuration, TestStartup.EnvironmentName);

        using var provider = services.BuildServiceProvider();

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ApiOptions>>().Value)
            .Message.ShouldContain("Api:Cors:AllowCredentials");
    }

    [Fact]
    public void LiveSettingsDoNotRequireARestart()
    {
        var applied = Settings();
        var current = Settings();

        current.Caches[0].DefaultTtlMinutes = 5;
        current.Databases[0].Sql.Outbox.BatchSize = 7;
        current.Databases[0].Sql.Outbox.PollIntervalSeconds = 3;

        SettingsReloadPolicy.RestartRequiredChanges(AppSettings.SectionName, applied, current).ShouldBeEmpty();
    }

    [Fact]
    public void StartupOnlySettingsAreReportedByKey()
    {
        var applied = Settings();
        var current = Settings();

        current.Databases[0].Host = "elsewhere";
        current.Caches.Add(new CacheSettings { Id = "EXTRA" });

        SettingsReloadPolicy.RestartRequiredChanges(AppSettings.SectionName, applied, current)
            .ShouldBe(["Settings:Databases[0]:Host", "Settings:Caches"], ignoreOrder: true);
    }

    [Fact]
    public async Task TheWatcherWarnsWhenAStartupOnlySettingChangesAtRuntime()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Settings:Caches:0:Id"] = "DEFAULT",
            ["Settings:Caches:0:Type"] = "Memory",
        }).Build();

        var startup = TestStartup.From(configuration);
        var logs = new SecretOptionsPipelineTests.CapturingLoggerProvider();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddSingleton<IHostApplicationLifetime, StubApplicationLifetime>();
        services.AddCrossCuttingSetup(configuration, startup, NullLogger.Instance);

        await using var provider = services.BuildServiceProvider();

        var watcher = provider.GetServices<IHostedService>().OfType<RestartRequiredSettingsWatcher>().Single();
        await watcher.StartAsync(CancellationToken.None);

        configuration["Settings:Caches:0:DefaultTtlMinutes"] = "15";
        configuration.Reload();

        logs.Entries.Select(entry => entry.Message).ShouldNotContain(message => message.Contains("restart the service", StringComparison.Ordinal));

        configuration["Settings:Caches:0:Type"] = "Redis";
        configuration["Settings:Caches:0:Host"] = "redis";
        configuration.Reload();

        logs.Entries.Select(entry => entry.Message).ShouldContain(message =>
            message.Contains("restart the service", StringComparison.Ordinal)
            && message.Contains("Settings:Caches[0]:Type", StringComparison.Ordinal));
    }

    private static AppSettings Settings() => new()
    {
        Databases = [new DatabaseSettings { Id = "DEFAULT", Type = DatabaseType.Postgresql, Host = "db" }],
        Caches = [new CacheSettings { Id = "DEFAULT" }],
    };

    private static IConfigurationRoot Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static ServiceProvider Container(
        IConfiguration configuration,
        StartupSettings startup,
        FakeEnvironmentAccessor environment)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostApplicationLifetime, StubApplicationLifetime>();
        services.AddCrossCuttingSetup(configuration, startup, NullLogger.Instance);
        services.AddSingleton<IEnvironmentAccessor>(environment);

        return services.BuildServiceProvider();
    }
}