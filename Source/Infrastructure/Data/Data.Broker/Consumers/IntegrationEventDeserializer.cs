using System.Text;
using System.Text.Json;
using Domain.Extensions;
using Domain.Interfaces.Integration;

namespace Data.Broker.Consumers;

public sealed class IntegrationEventDeserializer(IIntegrationEventRegistry registry)
{
    public IIntegrationEvent? Deserialize(ReadOnlySpan<byte> body, string? declaredEventType)
    {
        var json = Encoding.UTF8.GetString(body);

        var eventType = declaredEventType;

        if (string.IsNullOrWhiteSpace(eventType))
        {
            eventType = ReadEventTypeFromPayload(json);
        }

        if (string.IsNullOrWhiteSpace(eventType) || !registry.TryResolve(eventType, out var clrType))
        {
            return null;
        }

        return JsonSerializer.Deserialize(json, clrType, JsonDefaults.Standard) as IIntegrationEvent;
    }

    private static string? ReadEventTypeFromPayload(string json)
    {
        using var document = JsonDocument.Parse(json);

        foreach (var name in (string[])["eventType", "EventType"])
        {
            if (document.RootElement.TryGetProperty(name, out var element)
                && element.ValueKind == JsonValueKind.String)
            {
                return element.GetString();
            }
        }

        return null;
    }
}
