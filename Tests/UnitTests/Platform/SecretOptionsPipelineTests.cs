using System.Collections.Concurrent;
using System.Net;
using CrossCutting.Setup;
using Domain.Enums;
using Domain.Interfaces.Integration;
using Domain.Models.Configuration;
using Domain.Models.Requests;
using Domain.Setup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UnitTests.Modules;
using UnitTests.TestSupport;

namespace UnitTests.Platform;

public sealed class SecretOptionsPipelineTests : IDisposable
{
    private const string AuthorizationKey = "Settings:Apis:0:AuthorizationValue";

    private readonly string _secretDirectory =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "secret-pipeline-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(_secretDirectory, recursive: true);

    [Fact]
    public void OptionsMonitorServesTheResolvedSecretNotTheFilePath()
    {
        var secret = WriteSecret("api-token", "token-from-file");

        using var provider = BuildPipeline(Partner(secret)).Provider;

        provider.GetRequiredService<IOptionsMonitor<AppSettings>>()
            .CurrentValue.Apis[0].AuthorizationValue.ShouldBe("token-from-file");
    }

    [Fact]
    public void OptionsAndSnapshotServeTheResolvedSecret()
    {
        var secret = WriteSecret("api-token", "token-from-file");

        using var provider = BuildPipeline(Partner(secret)).Provider;

        provider.GetRequiredService<IOptions<AppSettings>>()
            .Value.Apis[0].AuthorizationValue.ShouldBe("token-from-file");

        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<AppSettings>>()
            .Value.Apis[0].AuthorizationValue.ShouldBe("token-from-file");
    }

    [Fact]
    public void AReloadIsResolvedAgainAndNotifiesListeners()
    {
        var first = WriteSecret("first", "first-token");
        var second = WriteSecret("second", "second-token");

        var (provider, configuration) = BuildPipeline(Partner(first));

        using (provider)
        {
            var monitor = provider.GetRequiredService<IOptionsMonitor<AppSettings>>();

            monitor.CurrentValue.Apis[0].AuthorizationValue.ShouldBe("first-token");

            string? notified = null;

            using var subscription = monitor.OnChange((settings, _) =>
                notified = settings.Apis[0].AuthorizationValue);

            configuration[AuthorizationKey] = second;
            configuration.Reload();

            monitor.CurrentValue.Apis[0].AuthorizationValue.ShouldBe("second-token");
            notified.ShouldBe("second-token");
        }
    }

    [Fact]
    public void ABrokenReloadKeepsTheLastValidValueAndLogsAnError()
    {
        var secret = WriteSecret("api-token", "good-token");
        var logs = new CapturingLoggerProvider();

        var (provider, configuration) = BuildPipeline(Partner(secret), logs);

        using (provider)
        {
            var monitor = provider.GetRequiredService<IOptionsMonitor<AppSettings>>();

            monitor.CurrentValue.Apis[0].AuthorizationValue.ShouldBe("good-token");

            configuration[AuthorizationKey] = Path.Combine(_secretDirectory, "missing");

            Should.NotThrow(configuration.Reload);

            monitor.CurrentValue.Apis[0].AuthorizationValue.ShouldBe("good-token");

            logs.Entries.ShouldContain(entry =>
                entry.Level == LogLevel.Error && entry.Message.Contains("AppSettings", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ABrokenReloadThatIsLaterFixedAppliesTheFix()
    {
        var secret = WriteSecret("api-token", "good-token");
        var fixedSecret = WriteSecret("fixed", "fixed-token");

        var (provider, configuration) = BuildPipeline(Partner(secret), new CapturingLoggerProvider());

        using (provider)
        {
            var monitor = provider.GetRequiredService<IOptionsMonitor<AppSettings>>();

            _ = monitor.CurrentValue;

            configuration[AuthorizationKey] = Path.Combine(_secretDirectory, "missing");
            configuration.Reload();

            configuration[AuthorizationKey] = fixedSecret;
            configuration.Reload();

            monitor.CurrentValue.Apis[0].AuthorizationValue.ShouldBe("fixed-token");
        }
    }

    [Fact]
    public void InvalidConnectionListsFailStartupValidationWithTheKey()
    {
        var values = new Dictionary<string, string?>
        {
            ["Settings:Databases:0:Id"] = "DEFAULT",
            ["Settings:Databases:1:Id"] = "default",
        };

        using var provider = BuildPipeline(values).Provider;

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate())
            .Message.ShouldContain("Settings:Databases declares duplicate");
    }

    [Fact]
    public void AMissingSecretFileFailsStartupValidation()
    {
        using var provider = BuildPipeline(Partner(Path.Combine(_secretDirectory, "missing"))).Provider;

        Should.Throw<Exception>(() => provider.GetRequiredService<IStartupValidator>().Validate())
            .ToString().ShouldContain(AuthorizationKey);
    }

    [Fact]
    public async Task RestApiClientSendsTheSecretFileContentsAsTheBearerToken()
    {
        var secret = WriteSecret("api-token", "token-from-file");
        var handler = new CapturingHandler();

        var values = Partner(secret);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<IHostApplicationLifetime, StubApplicationLifetime>();

        services.AddCrossCuttingSetup(
            configuration,
            TestStartup.From(configuration),
            NullLogger.Instance);

        services
            .AddHttpClient(NamedHttpClient.RestApi.ToString())
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        var provider = services.BuildServiceProvider();

        await using (provider.ConfigureAwait(true))
        {
            var result = await provider.GetRequiredService<IRestApiClient>()
                .SendRawAsync(new RestApiRequestModel { ApiId = "PARTNER", EndpointId = "PING" });

            result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Description : string.Empty);

            handler.Authorization.ShouldBe("Bearer token-from-file");
        }
    }

    private static (ServiceProvider Provider, IConfigurationRoot Configuration) BuildPipeline(
        Dictionary<string, string?> values,
        CapturingLoggerProvider? logs = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            if (logs is not null)
            {
                builder.AddProvider(logs);
            }
        });

        services.AddServicesSetup(configuration);
        services.AddDomainSetup(configuration, TestStartup.EnvironmentName);

        return (services.BuildServiceProvider(), configuration);
    }

    private static Dictionary<string, string?> Partner(string authorizationValue) => new()
    {
        ["Settings:Apis:0:Id"] = "PARTNER",
            ["Settings:Apis:0:Protocol"] = "Rest",
        ["Settings:Apis:0:BaseAddress"] = "https://partner.test/",
        ["Settings:Apis:0:AuthorizationType"] = "Bearer",
        [AuthorizationKey] = authorizationValue,
        ["Settings:Apis:0:Endpoints:0:Id"] = "PING",
        ["Settings:Apis:0:Endpoints:0:Path"] = "/ping",
    };

    private string WriteSecret(string name, string value)
    {
        var path = Path.Combine(_secretDirectory, name);

        File.WriteAllText(path, value + "\n");

        return path;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}"),
            });
        }
    }

    internal sealed record LogEntry(LogLevel Level, string Message);

    internal sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new LogEntry(logLevel, formatter(state, exception)));
        }
    }
}
