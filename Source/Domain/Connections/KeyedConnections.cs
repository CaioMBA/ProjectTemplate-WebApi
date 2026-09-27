using Microsoft.Extensions.DependencyInjection;

namespace Domain.Connections;

public static class KeyedConnections
{
    public const string Databases = "Databases";

    public const string Caches = "Caches";

    public const string Brokers = "Brokers";

    public static T RequireKeyed<T>(this IServiceProvider provider, string id, string listName)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return provider.GetKeyedService<T>(id)
            ?? throw new InvalidOperationException(
                $"No {typeof(T).Name} is registered under Id '{id}'. Add an entry with that Id to "
                + $"Settings:{listName} (the Id is matched exactly, including case).");
    }
}