using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OperaHouse.Booking.Infrastructure.Persistence;
using OperaHouse.Messaging;

namespace OperaHouse.Booking.Infrastructure.Outbox;

public sealed class OutboxPublisherWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxPublisherWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan DelayBetweenRuns = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox publisher worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishPendingMessagesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown.
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Outbox publisher worker failed while publishing messages.");
            }

            await Task.Delay(
                DelayBetweenRuns,
                stoppingToken);
        }
    }

    private async Task PublishPendingMessagesAsync(
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<BookingDbContext>();

        var publisher = scope.ServiceProvider
            .GetRequiredService<RabbitMqPublisher>();

        var messages = await dbContext.OutboxMessages
            .Where(message => message.ProcessedAt == null)
            .OrderBy(message => message.OccurredAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            return;
        }

        foreach (var message in messages)
        {
            try
            {
                await publisher.PublishJsonAsync(
                    payload: message.Payload,
                    routingKey: message.RoutingKey,
                    messageId: message.MessageId,
                    messageType: message.Type,
                    cancellationToken: cancellationToken);

                message.ProcessedAt = DateTimeOffset.UtcNow;
                message.LastError = null;

                logger.LogInformation(
                    "Published outbox message {MessageId} of type {MessageType}.",
                    message.MessageId,
                    message.Type);
            }
            catch (Exception exception)
            {
                message.PublishAttempts++;
                message.LastError = exception.Message;

                logger.LogError(
                    exception,
                    "Failed to publish outbox message {MessageId}. Attempt {Attempt}.",
                    message.MessageId,
                    message.PublishAttempts);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
