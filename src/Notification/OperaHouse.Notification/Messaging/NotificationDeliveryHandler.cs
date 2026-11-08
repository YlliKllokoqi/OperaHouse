using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using OperaHouse.Contracts.Events;
using OperaHouse.Messaging;
using OperaHouse.Notification.Email;
using OperaHouse.Notification.Features;
using OperaHouse.Notification.Processing;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OperaHouse.Notification.Messaging;

internal sealed class NotificationDeliveryHandler(
    ILogger logger, IServiceScopeFactory scopes, RabbitMqOptions options)
{
    public Task HandleBookingCreatedAsync(IChannel channel, IChannel retryChannel,
        BasicDeliverEventArgs delivery, CancellationToken token) =>
        HandleAsync(channel, retryChannel, delivery, nameof(BookingCreated),
            options.BookingCreatedRetryRoutingKey, options.BookingCreatedMaxRetryAttempts,
            async (services, context) =>
            {
                var message = JsonSerializer.Deserialize<BookingCreated>(delivery.Body.Span)
                    ?? throw new JsonException("BookingCreated message could not be deserialized.");
                context["MessageId"] = message.MessageId;
                context["CorrelationId"] = message.CorrelationId;
                context["BookingId"] = message.BookingId;
                return await services.GetRequiredService<SendBookingConfirmation>().HandleAsync(message, token);
            }, token);

    public Task HandleBookingExpiredAsync(IChannel channel, IChannel retryChannel,
        BasicDeliverEventArgs delivery, CancellationToken token) =>
        HandleAsync(channel, retryChannel, delivery, nameof(BookingExpired),
            options.BookingExpiredRetryRoutingKey, options.BookingExpiredMaxRetryAttempts,
            async (services, context) =>
            {
                var message = JsonSerializer.Deserialize<BookingExpired>(delivery.Body.Span)
                    ?? throw new JsonException("BookingExpired message could not be deserialized.");
                context["MessageId"] = message.MessageId;
                context["CorrelationId"] = message.CorrelationId;
                context["BookingId"] = message.BookingId;
                return await services.GetRequiredService<SendBookingExpiration>().HandleAsync(message, token);
            }, token);

    public Task HandleBookingCancelledAsync(IChannel channel, IChannel retryChannel,
        BasicDeliverEventArgs delivery, CancellationToken token) =>
        HandleAsync(channel, retryChannel, delivery, nameof(BookingCancelled),
            options.BookingCancelledRetryRoutingKey, options.BookingCancelledMaxRetryAttempts,
            async (services, context) =>
            {
                var message = JsonSerializer.Deserialize<BookingCancelled>(delivery.Body.Span)
                    ?? throw new JsonException("BookingCancelled message could not be deserialized.");
                context["MessageId"] = message.MessageId;
                context["CorrelationId"] = message.CorrelationId;
                context["BookingId"] = message.BookingId;
                return await services.GetRequiredService<SendBookingCancellation>().HandleAsync(message, token);
            }, token);

    private async Task HandleAsync(IChannel channel, IChannel retryChannel, BasicDeliverEventArgs delivery,
        string messageType, string retryRoutingKey, int maxAttempts,
        Func<IServiceProvider, Dictionary<string, object?>, Task<NotificationProcessingResult>> process,
        CancellationToken token)
    {
        var retryCount = GetRetryCount(delivery.BasicProperties);
        var context = new Dictionary<string, object?>
        {
            ["MessageType"] = messageType,
            ["MessageId"] = delivery.BasicProperties.MessageId,
            ["CorrelationId"] = delivery.BasicProperties.CorrelationId,
            ["RoutingKey"] = delivery.RoutingKey,
            ["RetryCount"] = retryCount
        };
        using var loggingScope = logger.BeginScope(context);

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await process(scope.ServiceProvider, context);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken: token);
            logger.LogInformation("Processed {MessageType} notification: {Result}.", messageType, result);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Leave the delivery unacknowledged during shutdown.
        }
        catch (Exception exception) when (exception is JsonException or ValidationException)
        {
            logger.LogWarning(exception, "Invalid {MessageType} message rejected to the dead-letter queue.", messageType);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false,
                cancellationToken: token);
        }
        catch (PermanentEmailDeliveryException exception)
        {
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false,
                cancellationToken: token);
            logger.LogError(exception,
                "Rejected {MessageType} notification after a permanent email failure.", messageType);
        }
        catch (Exception exception)
        {
            if (retryCount < maxAttempts)
            {
                await PublishRetryAsync(retryChannel, delivery, exception, retryCount + 1, retryRoutingKey, token);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken: token);
                logger.LogWarning(exception, "{MessageType} retry {RetryAttempt}/{MaxRetryAttempts} scheduled.",
                    messageType, retryCount + 1, maxAttempts);
                return;
            }

            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false,
                cancellationToken: token);
            logger.LogError(exception, "{MessageType} retries exhausted; message rejected to the dead-letter queue.", messageType);
        }
    }

    private async Task PublishRetryAsync(IChannel channel, BasicDeliverEventArgs delivery,
        Exception exception, int retryCount, string routingKey, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        var headers = delivery.BasicProperties.Headers is { } original
            ? new Dictionary<string, object?>(original)
            : new Dictionary<string, object?>();
        headers.TryAdd("x-original-exchange", delivery.Exchange);
        headers.TryAdd("x-original-routing-key", delivery.RoutingKey);
        headers.TryAdd("x-first-failed-at", now);
        headers["x-retry-count"] = retryCount;
        headers["x-last-failed-at"] = now;
        headers["x-last-error"] = exception.Message;

        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = delivery.BasicProperties.ContentType ?? "application/json",
            MessageId = delivery.BasicProperties.MessageId,
            CorrelationId = delivery.BasicProperties.CorrelationId,
            Type = delivery.BasicProperties.Type,
            Timestamp = delivery.BasicProperties.Timestamp,
            Headers = headers
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await channel.BasicPublishAsync(options.RetryExchangeName, routingKey, mandatory: true,
            basicProperties: properties, body: delivery.Body, cancellationToken: timeout.Token);
    }

    private static int GetRetryCount(IReadOnlyBasicProperties properties)
    {
        if (properties.Headers is null || !properties.Headers.TryGetValue("x-retry-count", out var value))
            return 0;

        return value switch
        {
            int number => number,
            long number => (int)number,
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
            _ => 0
        };
    }
}
