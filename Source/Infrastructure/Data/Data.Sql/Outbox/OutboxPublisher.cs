using System.Diagnostics;
using System.Text.Json;
using Data.Sql.EntityFrameworkContexts;
using Domain.Abstractions;
using Domain.Extensions;
using Domain.Interfaces.Broker;
using Domain.Interfaces.Integration;
using Domain.Models.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Data.Sql.Outbox;

public sealed class OutboxPublisher(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<AppSettings> settings,
    string databaseId,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    private static readonly ActivitySource _activitySource = new(TelemetryNames.OutboxActivitySource);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox publisher for database {DatabaseId} started.", databaseId);

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = CurrentOptions();

            try
            {
                await PublishPendingAsync(options, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031
            catch (Exception exception)
#pragma warning restore CA1031
            {
                logger.LogError(
                    exception,
                    "The outbox publisher iteration for database {DatabaseId} failed; retrying next tick.",
                    databaseId);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.PollIntervalSeconds)), stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Outbox publisher for database {DatabaseId} stopped.", databaseId);
    }

    private SqlOutboxOptions CurrentOptions() =>
        settings.CurrentValue.GetDatabase(databaseId).Sql.Outbox;

    private async Task PublishPendingAsync(SqlOutboxOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.BrokerId))
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();

        var eventBus = scope.ServiceProvider.GetKeyedService<IEventBus>(options.BrokerId);

        if (eventBus is null)
        {
            logger.LogWarning(
                "Outbox for database {DatabaseId} targets broker {BrokerId}, which is not registered; rows stay pending.",
                databaseId,
                options.BrokerId);

            return;
        }

        var context = scope.ServiceProvider.GetRequiredKeyedService<AppDbContext>(databaseId);

        var now = DateTime.UtcNow;

        var messages = await context.OutboxMessages
            .FromSqlRaw(
                """
                SELECT * FROM outbox_messages
                WHERE processed_on_utc IS NULL
                  AND (next_attempt_utc IS NULL OR next_attempt_utc <= {0})
                ORDER BY occurred_on_utc
                LIMIT {1}
                FOR UPDATE SKIP LOCKED
                """,
                now,
                Math.Max(1, options.BatchSize))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (messages.Count == 0)
        {
            return;
        }

        foreach (var message in messages)
        {
            await PublishOneAsync(eventBus, message, now, cancellationToken).ConfigureAwait(false);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
    private async Task PublishOneAsync(
        IEventBus eventBus,
        Domain.Entities.OutboxMessageEntity message,
        DateTime now,
        CancellationToken cancellationToken)
    {
        using var activity = _activitySource.StartActivity(
            $"Publish {message.EventType}",
            ActivityKind.Producer,
            message.TraceParent);

        try
        {
            var type = Type.GetType(message.ContentType)
                       ?? throw new InvalidOperationException(
                           $"Outbox row {message.Id} names CLR type '{message.ContentType}', which is not " +
                           $"loadable in this process. The event contract may have been renamed or moved.");

            if (JsonSerializer.Deserialize(message.Payload, type, JsonDefaults.Standard)
                is not IIntegrationEvent integrationEvent)
            {
                throw new InvalidOperationException(
                    $"Outbox row {message.Id} deserialised to '{type.Name}', which is not an IIntegrationEvent.");
            }

            await eventBus.PublishAsync(integrationEvent, cancellationToken).ConfigureAwait(false);

            message.ProcessedOnUtc = now;
            message.Error = null;

            logger.LogInformation(
                "Published outbox message {MessageId} of type {EventType}.",
                message.Id,
                message.EventType);
        }
#pragma warning disable CA1031
        catch (Exception exception)
#pragma warning restore CA1031
        {
            message.AttemptCount++;
            message.Error = exception.Message;

            var delaySeconds = Math.Min(3600, Math.Pow(2, Math.Min(message.AttemptCount, 12)));
            message.NextAttemptUtc = now.AddSeconds(delaySeconds);

            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);

            logger.LogError(
                exception,
                "Failed to publish outbox message {MessageId} (attempt {AttemptCount}); next attempt at {NextAttemptUtc}.",
                message.Id,
                message.AttemptCount,
                message.NextAttemptUtc);
        }
    }
}
