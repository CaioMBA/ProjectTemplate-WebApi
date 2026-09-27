using System.Text.Json;
using Domain.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Observability.HealthChecks;

public static class HealthCheckResponseWriter
{
    public static Task WriteMinimalResponse(HttpContext context, HealthReport report) =>
        WriteResponse(context, report, includeDetails: false);

    public static Task WriteDetailedResponse(HttpContext context, HealthReport report) =>
        WriteResponse(context, report, includeDetails: true);

    private static async Task WriteResponse(HttpContext context, HealthReport report, bool includeDetails)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        context.Response.ContentType = "application/json; charset=utf-8";

        using var stream = new MemoryStream();

        await using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            writer.WriteString("status", report.Status.ToString());
            writer.WriteNumber("totalDurationMs", report.TotalDuration.TotalMilliseconds);

            if (includeDetails)
            {
                writer.WriteStartObject("entries");

                foreach (var (name, entry) in report.Entries)
                {
                    writer.WriteStartObject(name);
                    writer.WriteString("status", entry.Status.ToString());
                    writer.WriteNumber("durationMs", entry.Duration.TotalMilliseconds);

                    if (!string.IsNullOrWhiteSpace(entry.Description))
                    {
                        writer.WriteString("description", entry.Description);
                    }

                    if (entry.Tags.Any())
                    {
                        writer.WriteStartArray("tags");

                        foreach (var tag in entry.Tags)
                        {
                            writer.WriteStringValue(tag);
                        }

                        writer.WriteEndArray();
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        await context.Response.Body
            .WriteAsync(stream.ToArray(), context.RequestAborted)
            .ConfigureAwait(false);
    }

    public static JsonSerializerOptions SerializerOptions => JsonDefaults.Standard;
}
