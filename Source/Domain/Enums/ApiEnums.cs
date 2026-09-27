using System.Text.Json.Serialization;

namespace Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<ApiProtocolType>))]
public enum ApiProtocolType
{
    Rest = 0,
    GraphQl = 1,
    Grpc = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter<ApiRequestMethod>))]
public enum ApiRequestMethod
{
    Get = 0,
    Post = 1,
    Put = 2,
    Patch = 3,
    Delete = 4,
    Head = 5,
    Options = 6,
}

[JsonConverter(typeof(JsonStringEnumConverter<ApiAuthorizationType>))]
public enum ApiAuthorizationType
{
    None = 0,
    Basic = 1,
    Bearer = 2,
    ApiKey = 3,
}
