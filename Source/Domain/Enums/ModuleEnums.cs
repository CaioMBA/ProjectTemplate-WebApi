using System.Text.Json.Serialization;

namespace Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<CacheType>))]
public enum CacheType
{
    Memory = 0,
    Redis = 1,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrokerType>))]
public enum BrokerType
{
    RabbitMq = 0,
    Kafka = 1,
}

[JsonConverter(typeof(JsonStringEnumConverter<NamedHttpClient>))]
public enum NamedHttpClient
{
    RestApi = 0,
    GraphqlApi = 1,
    GrpcApi = 2,
}
