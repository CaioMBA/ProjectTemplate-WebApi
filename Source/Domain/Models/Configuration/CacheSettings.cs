using Domain.Attributes;
using Domain.Enums;

namespace Domain.Models.Configuration;

public sealed class CacheSettings
{
    public const int FallbackTtlMinutes = 60;

    public required string Id { get; set; }

    public CacheType Type { get; set; } = CacheType.Memory;

    public string? Host { get; set; }

    public int? Port { get; set; }

    public string? Username { get; set; }

    [Secret]
    public string? Password { get; set; }

    public bool UseSsl { get; set; }

    public int TimeoutSeconds { get; set; } = 5;

    public int DefaultTtlMinutes { get; set; } = FallbackTtlMinutes;

    public RedisCacheOptions Redis { get; set; } = new();

    public string KeyOf(string property) =>
        $"{AppSettings.SectionName}:{nameof(AppSettings.Caches)}[{nameof(Id)}={Id}]:{property}";
}

public sealed class RedisCacheOptions
{
    public int Database { get; set; }

    public string InstanceName { get; set; } = "webapi-template:";
}
