using Domain.Attributes;
using Domain.Enums;

namespace Domain.Models.Configuration;

public sealed class DatabaseSettings
{
    public required string Id { get; set; }

    public DatabaseType Type { get; set; } = DatabaseType.Postgresql;

    [Secret]
    public string? ConnectionString { get; set; }

    public string? Host { get; set; }

    public int? Port { get; set; }

    public string? Database { get; set; }

    public string? Username { get; set; }

    [Secret]
    public string? Password { get; set; }

    public bool UseSsl { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    public SqlDatabaseOptions Sql { get; set; } = new();

    public CloudConnectionOptions Cloud { get; set; } = new();

    public ClusterConnectionOptions Cluster { get; set; } = new();

    public string KeyOf(string property) =>
        $"{AppSettings.SectionName}:{nameof(AppSettings.Databases)}[{nameof(Id)}={Id}]:{property}";
}

public sealed class SqlDatabaseOptions
{
    public int MaxPoolSize { get; set; } = 100;

    public bool MigrateOnStartup { get; set; }

    public bool LogInterpolatedSql { get; set; }

    public SqlOutboxOptions Outbox { get; set; } = new();

    public SqlPagedCacheOptions PagedCache { get; set; } = new();
}

public sealed class SqlOutboxOptions
{
    public bool Enabled { get; set; }

    public string? BrokerId { get; set; }

    public int PollIntervalSeconds { get; set; } = 10;

    public int BatchSize { get; set; } = 50;

    public int RetentionDays { get; set; } = 7;
}

public sealed class SqlPagedCacheOptions
{
    public string? CacheId { get; set; }

    public int MaxCachedPages { get; set; } = 50;

    public int DefaultTtlMinutes { get; set; } = 5;
}

public sealed class CloudConnectionOptions
{
    public string? Region { get; set; }

    public string? ServiceUrl { get; set; }

    [Secret]
    public string? AccessKey { get; set; }

    [Secret]
    public string? SecretKey { get; set; }

    public int MaxScanPageSize { get; set; } = 500;
}

public sealed class ClusterConnectionOptions
{
    public List<string> Urls { get; set; } = [];

    [Secret]
    public string? CertificatePath { get; set; }

    [Secret]
    public string? CertificatePassword { get; set; }
}
