using Domain.Enums;

namespace Domain.Models.Configuration;

public sealed class SchedulingSettings
{
    public bool Enabled { get; set; }

    public SchedulingStorageSettings Storage { get; set; } = new();

    public int Workers { get; set; } = 5;

    public List<string> Queues { get; set; } = ["default"];

    public int RetryAttempts { get; set; } = 3;

    public SchedulingDashboardSettings Dashboard { get; set; } = new();

    public List<ScheduledJobSettings> Jobs { get; set; } = [];
}

public sealed class SchedulingStorageSettings
{
    public SchedulingStorageType Type { get; set; } = SchedulingStorageType.Memory;

    public string? DatabaseId { get; set; }

    public string Schema { get; set; } = "hangfire";
}

public sealed class SchedulingDashboardSettings
{
    public bool Enabled { get; set; }

    public string Path { get; set; } = "/hangfire";

    public bool ReadOnly { get; set; } = true;
}

public sealed class ScheduledJobSettings
{
    public required string Id { get; set; }

    public string? Cron { get; set; }

    public bool Enabled { get; set; } = true;

    public string TimeZone { get; set; } = "UTC";
}