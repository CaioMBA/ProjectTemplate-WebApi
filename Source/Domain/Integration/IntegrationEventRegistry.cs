using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Domain.Interfaces.Integration;

namespace Domain.Integration;

public sealed class IntegrationEventRegistry : IIntegrationEventRegistry
{
    private readonly Dictionary<string, Type> _typesByEventType;

    public IntegrationEventRegistry(IEnumerable<Type> eventTypes)
    {
        ArgumentNullException.ThrowIfNull(eventTypes);

        _typesByEventType = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var type in eventTypes)
        {
            var eventType = ReadEventType(type);

            if (_typesByEventType.TryGetValue(eventType, out var existing))
            {
                throw new InvalidOperationException(
                    $"Integration event type '{eventType}' is declared by both "
                    + $"{existing.Name} and {type.Name}. EventType must be unique.");
            }

            _typesByEventType[eventType] = type;
        }
    }

    public IReadOnlyCollection<string> KnownEventTypes => _typesByEventType.Keys;

    public static IntegrationEventRegistry FromAssemblies(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var types = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: false, IsInterface: false }
                && typeof(IIntegrationEvent).IsAssignableFrom(type));

        return new IntegrationEventRegistry(types);
    }

    public bool TryResolve(string eventType, [NotNullWhen(true)] out Type? clrType) =>
        _typesByEventType.TryGetValue(eventType, out clrType);

    private static string ReadEventType(Type type)
    {
        var probe = (IIntegrationEvent)RuntimeHelpers.GetUninitializedObject(type);

        var eventType = probe.EventType;

        return string.IsNullOrWhiteSpace(eventType)
            ? throw new InvalidOperationException(
                $"Integration event '{type.Name}' returns an empty EventType. EventType must be "
                + "a constant discriminator such as \"product.created\".")
            : eventType;
    }
}
