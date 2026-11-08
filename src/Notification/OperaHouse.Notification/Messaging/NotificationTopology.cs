using OperaHouse.Messaging;
using RabbitMQ.Client;

namespace OperaHouse.Notification.Messaging;

internal static class NotificationTopology
{
    public static async Task DeclareAsync(IChannel channel, RabbitMqOptions options, CancellationToken token)
    {
        await channel.ExchangeDeclareAsync(options.ExchangeName, ExchangeType.Topic,
            durable: true, autoDelete: false, arguments: null, cancellationToken: token);
        await channel.ExchangeDeclareAsync(options.DeadLetterExchangeName, ExchangeType.Topic,
            durable: true, autoDelete: false, arguments: null, cancellationToken: token);
        await channel.ExchangeDeclareAsync(options.RetryExchangeName, ExchangeType.Topic,
            durable: true, autoDelete: false, arguments: null, cancellationToken: token);

        await DeclareQueuesAsync(channel, options, options.BookingCreatedQueueName,
            options.BookingCreatedRoutingKey, options.BookingCreatedRetryQueueName,
            options.BookingCreatedRetryRoutingKey, options.BookingCreatedRetryDelayMilliseconds,
            options.BookingCreatedDeadLetterQueueName, options.BookingCreatedDeadLetterRoutingKey, token);
        await DeclareQueuesAsync(channel, options, options.BookingExpiredQueueName,
            options.BookingExpiredRoutingKey, options.BookingExpiredRetryQueueName,
            options.BookingExpiredRetryRoutingKey, options.BookingExpiredRetryDelayMilliseconds,
            options.BookingExpiredDeadLetterQueueName, options.BookingExpiredDeadLetterRoutingKey, token);
        await DeclareQueuesAsync(channel, options, options.BookingCancelledQueueName,
            options.BookingCancelledRoutingKey, options.BookingCancelledRetryQueueName,
            options.BookingCancelledRetryRoutingKey, options.BookingCancelledRetryDelayMilliseconds,
            options.BookingCancelledDeadLetterQueueName, options.BookingCancelledDeadLetterRoutingKey, token);
    }

    private static async Task DeclareQueuesAsync(IChannel channel, RabbitMqOptions options,
        string queue, string routingKey, string retryQueue, string retryRoutingKey, int retryDelay,
        string deadLetterQueue, string deadLetterRoutingKey, CancellationToken token)
    {
        await channel.QueueDeclareAsync(deadLetterQueue, durable: true, exclusive: false,
            autoDelete: false, arguments: null, cancellationToken: token);
        await channel.QueueBindAsync(deadLetterQueue, options.DeadLetterExchangeName,
            deadLetterRoutingKey, arguments: null, cancellationToken: token);

        var queueArguments = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = options.DeadLetterExchangeName,
            ["x-dead-letter-routing-key"] = deadLetterRoutingKey
        };
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false,
            autoDelete: false, arguments: queueArguments, cancellationToken: token);
        await channel.QueueBindAsync(queue, options.ExchangeName, routingKey,
            arguments: null, cancellationToken: token);

        var retryArguments = new Dictionary<string, object?>
        {
            ["x-message-ttl"] = retryDelay,
            ["x-dead-letter-exchange"] = options.ExchangeName,
            ["x-dead-letter-routing-key"] = routingKey
        };
        await channel.QueueDeclareAsync(retryQueue, durable: true, exclusive: false,
            autoDelete: false, arguments: retryArguments, cancellationToken: token);
        await channel.QueueBindAsync(retryQueue, options.RetryExchangeName, retryRoutingKey,
            arguments: null, cancellationToken: token);
    }
}
