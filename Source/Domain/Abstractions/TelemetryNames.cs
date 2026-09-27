namespace Domain.Abstractions;

public static class TelemetryNames
{
    public const string ApplicationActivitySource = "WebApiTemplate.Application";

    public const string IntegrationActivitySource = "WebApiTemplate.Integration";

    public const string OutboxActivitySource = "WebApiTemplate.Outbox";

    public const string ApplicationMeter = "WebApiTemplate.Application";

    public const string RequestNameTag = "app.request.name";

    public const string RequestSuccessTag = "app.request.success";

    public const string RequestErrorCodeTag = "app.request.error_code";
}
