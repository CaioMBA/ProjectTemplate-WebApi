using Domain.Models.Configuration;
using Domain.Setup;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrossCutting.Configuration;

public sealed partial class RestartRequiredSettingsWatcher(
    StartupSettings startup,
    IOptionsMonitor<AppSettings> settings,
    IOptionsMonitor<ApiOptions> api,
    IOptionsMonitor<ObservabilityOptions> observability,
    ILogger<RestartRequiredSettingsWatcher> logger) : IHostedService, IDisposable
{
    private readonly List<IDisposable> _subscriptions = [];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Watch(settings, AppSettings.SectionName, startup.Settings);
        Watch(api, ApiOptions.SectionName, startup.Api);
        Watch(observability, ObservabilityOptions.SectionName, startup.Observability);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    private void Watch<T>(IOptionsMonitor<T> monitor, string sectionName, T applied)
        where T : class
    {
        var subscription = monitor.OnChange((current, _) =>
        {
            var changes = SettingsReloadPolicy.RestartRequiredChanges(sectionName, applied, current);

            if (changes.Count > 0)
            {
                LogRestartRequired(logger, string.Join(", ", changes));
            }
        });

        if (subscription is not null)
        {
            _subscriptions.Add(subscription);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Configuration changed in settings that only apply at startup; restart the service to apply: {Keys}")]
    private static partial void LogRestartRequired(ILogger logger, string keys);
}