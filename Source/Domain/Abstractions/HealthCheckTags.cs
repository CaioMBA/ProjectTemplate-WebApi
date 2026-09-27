namespace Domain.Abstractions;

public static class HealthCheckTags
{
    public const string Self = "self";

    public const string Api = "api";

    public const string Ready = "ready";

    public const string Critical = "critical";

    public const string Database = "db";

    public const string NoSql = "nosql";

    public const string Cache = "cache";

    public const string Broker = "broker";

    public const string Scheduler = "scheduler";
}