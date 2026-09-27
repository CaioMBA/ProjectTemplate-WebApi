namespace Data.Broker.Providers.Kafka;

public static class KafkaTopics
{
    public static string For(string topicPrefix, string eventType) =>
        string.IsNullOrWhiteSpace(topicPrefix) ? eventType : $"{topicPrefix}.{eventType}";

    public static string EventTypeOf(string topicPrefix, string topic)
    {
        ArgumentNullException.ThrowIfNull(topic);

        if (string.IsNullOrWhiteSpace(topicPrefix))
        {
            return topic;
        }

        var prefix = $"{topicPrefix}.";

        return topic.StartsWith(prefix, StringComparison.Ordinal) ? topic[prefix.Length..] : topic;
    }
}
