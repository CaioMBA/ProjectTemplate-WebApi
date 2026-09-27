using System.Collections.Concurrent;
using Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace CrossCutting.Configuration;

public sealed class LastKnownGoodOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>, IDisposable
    where TOptions : class
{
    private readonly IOptionsFactory<TOptions> _factory;
    private readonly ILogger<LastKnownGoodOptionsMonitor<TOptions>> _logger;
    private readonly ConcurrentDictionary<string, TOptions> _values = new(StringComparer.Ordinal);
    private readonly List<IDisposable> _registrations = [];
    private readonly Lock _listenersGate = new();
    private readonly List<Action<TOptions, string?>> _listeners = [];

    public LastKnownGoodOptionsMonitor(
        IOptionsFactory<TOptions> factory,
        IEnumerable<IOptionsChangeTokenSource<TOptions>> sources,
        ILogger<LastKnownGoodOptionsMonitor<TOptions>> logger)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(logger);

        _factory = factory;
        _logger = logger;

        foreach (var source in sources)
        {
            _registrations.Add(ChangeToken.OnChange(source.GetChangeToken, Reload, source.Name));
        }
    }

    public TOptions CurrentValue => Get(Options.DefaultName);

    public TOptions Get(string? name) =>
        _values.GetOrAdd(name ?? Options.DefaultName, _factory.Create);

    public IDisposable? OnChange(Action<TOptions, string?> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);

        lock (_listenersGate)
        {
            _listeners.Add(listener);
        }

        return new Subscription(this, listener);
    }

    public void Dispose()
    {
        foreach (var registration in _registrations)
        {
            registration.Dispose();
        }

        _registrations.Clear();
    }

    private void Reload(string? name)
    {
        name ??= Options.DefaultName;

        TOptions fresh;

        try
        {
            fresh = _factory.Create(name);
        }
        catch (Exception exception) when (IsConfigurationFailure(exception))
        {
            _logger.LogError(
                exception,
                "Reloading {OptionsType} failed, so the last valid value stays in use. "
                + "Fix the configuration to apply the change.",
                typeof(TOptions).Name);

            return;
        }

        _values[name] = fresh;

        Action<TOptions, string?>[] listeners;

        lock (_listenersGate)
        {
            listeners = [.. _listeners];
        }

        foreach (var listener in listeners)
        {
            listener(fresh, name);
        }
    }

    private static bool IsConfigurationFailure(Exception exception) =>
        exception is OptionsValidationException
            or SecretResolutionException
            or InvalidOperationException
            or FormatException;

    private void RemoveListener(Action<TOptions, string?> listener)
    {
        lock (_listenersGate)
        {
            _listeners.Remove(listener);
        }
    }

    private sealed class Subscription(
        LastKnownGoodOptionsMonitor<TOptions> owner,
        Action<TOptions, string?> listener) : IDisposable
    {
        public void Dispose() => owner.RemoveListener(listener);
    }
}
