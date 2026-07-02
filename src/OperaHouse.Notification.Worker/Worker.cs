using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OperaHouse.Contracts.Events;
using OperaHouse.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OperaHouse.Notification.Worker;

public class Worker(ILogger<Worker> logger,
    IOptions<RabbitMqOptions> options)
    : BackgroundService
{
    private readonly RabbitMqOptions _options = options.Value;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password
        };

        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        logger.LogInformation(
            "Declaring RabbitMQ exchange {ExchangeName}",
            _options.ExchangeName);
        
        //main exchange
        await channel.ExchangeDeclareAsync(
            exchange: _options.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: stoppingToken);
        
        logger.LogInformation(
            "Declaring RabbitMQ dead-letter exchange {ExchangeName}",
            _options.DeadLetterExchangeName);
        
        await channel.ExchangeDeclareAsync(
            exchange: _options.DeadLetterExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: stoppingToken);
        
        logger.LogInformation(
            "Declaring RabbitMQ retry exchange {ExchangeName}",
            _options.RetryExchangeName);
        
        await channel.ExchangeDeclareAsync(
            exchange: _options.RetryExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: stoppingToken);

        logger.LogInformation(
            "Declaring RabbitMQ dead-letter queue {QueueName}",
            _options.BookingCreatedDeadLetterQueueName);
        
        await channel.QueueDeclareAsync(
            queue: _options.BookingCreatedDeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: stoppingToken);

        logger.LogInformation(
            "Binding dead-letter queue {QueueName} to exchange {ExchangeName} using routing key {RoutingKey}",
            _options.BookingCreatedDeadLetterQueueName,
            _options.DeadLetterExchangeName,
            _options.BookingCreatedDeadLetterRoutingKey);
        
        await channel.QueueBindAsync(
            queue: _options.BookingCreatedDeadLetterQueueName,
            exchange: _options.DeadLetterExchangeName,
            routingKey: _options.BookingCreatedDeadLetterRoutingKey,
            arguments: null,
            cancellationToken: stoppingToken);
        
        logger.LogInformation(
            "Declaring RabbitMQ queue {QueueName}",
            _options.BookingCreatedQueueName);

        var queueArguments = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = _options.DeadLetterExchangeName,
            ["x-dead-letter-routing-key"] = _options.BookingCreatedDeadLetterRoutingKey
        };

        var retryQueueArguments = new Dictionary<string, object?>
        {
            ["x-message-ttl"] = _options.BookingCreatedRetryDelayMilliseconds,
            ["x-dead-letter-exchange"] = _options.ExchangeName,
            ["x-dead-letter-routing-key"] = _options.BookingCreatedRoutingKey
        };

        logger.LogInformation(
            "Main queue {QueueName} dead-letters to exchange {ExchangeName} using routing key {RoutingKey}",
            _options.BookingCreatedQueueName,
            _options.DeadLetterExchangeName,
            _options.BookingCreatedDeadLetterRoutingKey);
        
        await channel.QueueDeclareAsync(
            queue: _options.BookingCreatedQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArguments,
            cancellationToken: stoppingToken);
        
        logger.LogInformation(
            "Declaring retry queue {QueueName}. Messages wait {DelayMilliseconds} ms before returning to exchange {ExchangeName}",
            _options.BookingCreatedRetryQueueName,
            _options.BookingCreatedRetryDelayMilliseconds,
            _options.ExchangeName);
        
        await channel.QueueDeclareAsync(
            queue: _options.BookingCreatedRetryQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: retryQueueArguments,
            cancellationToken: stoppingToken);
        
        logger.LogInformation(
            "Binding queue {QueueName} to exchange {ExchangeName} using routing key {RoutingKey}",
            _options.BookingCreatedQueueName,
            _options.ExchangeName,
            _options.BookingCreatedRoutingKey);

        await channel.QueueBindAsync(
            queue: _options.BookingCreatedQueueName,
            exchange: _options.ExchangeName,
            routingKey: _options.BookingCreatedRoutingKey,
            arguments: null,
            cancellationToken: stoppingToken);
        
        logger.LogInformation(
            "Binding retry queue {QueueName} to retry exchange {ExchangeName} using routing key {RoutingKey}",
            _options.BookingCreatedRetryQueueName,
            _options.RetryExchangeName,
            _options.BookingCreatedRetryRoutingKey);
        
        await channel.QueueBindAsync(
            queue: _options.BookingCreatedRetryQueueName,
            exchange: _options.RetryExchangeName,
            routingKey: _options.BookingCreatedRetryRoutingKey,
            arguments: null,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);

        consumer.ReceivedAsync += async (_, eventArgs) =>
        {
            try
            {
                
                var json = Encoding.UTF8.GetString(
                    eventArgs.Body.ToArray());

                var message = JsonSerializer.Deserialize<BookingCreated>(json);

                if (message is null)
                {
                    throw new InvalidOperationException(
                        "BookingCreated message could not be deserialized.");
                }
                
                logger.LogInformation(
                    "Received BookingCreated message {MessageId} for booking {BookingId}",
                    message.MessageId,
                    message.BookingId);

                logger.LogInformation(
                    "Email sent to {CustomerEmail}: booking received",
                    message.CustomerEmail);
                
                logger.LogInformation(
                    "ACK sent for BookingCreated message {MessageId}",
                    message.MessageId);

                await channel.BasicAckAsync(
                    deliveryTag: eventArgs.DeliveryTag,
                    multiple: false,
                    cancellationToken: stoppingToken);
            }
            catch (Exception e)
            {
                logger.LogError(
                    e,
                    "Failed to process BookingCreated message.");

                var retryCount = GetRetryCount(eventArgs.BasicProperties);

                if (retryCount < _options.BookingCreatedMaxRetryAttempts)
                {
                    var nextRetryCount = retryCount + 1;

                    logger.LogWarning(
                        "BookingCreated message failed. Moving message to retry exchange {ExchangeName} with routing key {RoutingKey}. Retry attempt {RetryAttempt}/{MaxRetryAttempts}",
                        _options.RetryExchangeName,
                        _options.BookingCreatedRetryRoutingKey,
                        nextRetryCount,
                        _options.BookingCreatedMaxRetryAttempts);
                    
                    await PublishToRetryQueueAsync(
                        channel,
                        eventArgs.Body,
                        nextRetryCount,
                        stoppingToken);

                    await channel.BasicAckAsync(
                        deliveryTag: eventArgs.DeliveryTag,
                        multiple: false,
                        cancellationToken: stoppingToken);

                    logger.LogInformation(
                        "Original failed message was ACKed after being published to retry queue.");

                    return;
                }

                await channel.BasicNackAsync(
                    deliveryTag: eventArgs.DeliveryTag,
                    multiple: false,
                    requeue: false,
                    cancellationToken: stoppingToken);
                
                logger.LogWarning(
                    "BookingCreated message failed after {MaxRetryAttempts} retry attempts. NACK sent with requeue false, so RabbitMQ will move it to the dead-letter queue.",
                    _options.BookingCreatedMaxRetryAttempts);
            }
        };

        await channel.BasicConsumeAsync(
            queue: _options.BookingCreatedQueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);
        
        logger.LogInformation(
            "Notification worker is consuming queue {QueueName}",
            _options.BookingCreatedQueueName);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task PublishToRetryQueueAsync(
        IChannel channel,
        ReadOnlyMemory<byte> body,
        int retryCount,
        CancellationToken cancellationToken)
    {
        var retryProperties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            Headers = new Dictionary<string, object?>
            {
                ["x-retry-count"] = retryCount
            }
        };

        await channel.BasicPublishAsync(
            exchange: _options.RetryExchangeName,
            routingKey: _options.BookingCreatedRetryRoutingKey,
            mandatory: false,
            basicProperties: retryProperties,
            body: body,
            cancellationToken: cancellationToken);
    }

    private static int GetRetryCount(IReadOnlyBasicProperties properties)
    {
        if (properties.Headers is null)
        {
            return 0;
        }

        if (!properties.Headers.TryGetValue("x-retry-count", out var value))
        {
            return 0;
        }

        return value switch
        {
            int number => number,
            long number => (int)number,
            byte[] bytes when int.TryParse(
                Encoding.UTF8.GetString(bytes),
                out var parsed) => parsed,
            _ => 0
        };
    }
}
