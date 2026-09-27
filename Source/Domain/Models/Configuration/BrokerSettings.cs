using Domain.Attributes;
using Domain.Enums;

namespace Domain.Models.Configuration;

public sealed class BrokerSettings
{
    public required string Id { get; set; }

    public BrokerType Type { get; set; } = BrokerType.RabbitMq;

    public string? Host { get; set; }

    public int? Port { get; set; }

    public string? Username { get; set; }

    [Secret]
    public string? Password { get; set; }

    public bool UseSsl { get; set; }

    public bool EnableConsumer { get; set; }

    public RabbitMqBrokerOptions RabbitMq { get; set; } = new();

    public KafkaBrokerOptions Kafka { get; set; } = new();

    public string KeyOf(string property) =>
        $"{AppSettings.SectionName}:{nameof(AppSettings.Brokers)}[{nameof(Id)}={Id}]:{property}";
}

public sealed class RabbitMqBrokerOptions
{
    public string VirtualHost { get; set; } = "/";

    public string Exchange { get; set; } = "webapi-template.events";

    public string Queue { get; set; } = "webapi-template.inbox";
}

public sealed class KafkaBrokerOptions
{
    public string? BootstrapServers { get; set; }

    public string TopicPrefix { get; set; } = "webapi-template.events";

    public string ConsumerGroup { get; set; } = "webapi-template";
}
