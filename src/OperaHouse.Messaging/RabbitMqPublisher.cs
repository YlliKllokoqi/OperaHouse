using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace OperaHouse.Messaging;

public class RabbitMqPublisher(IOptions<RabbitMqOptions> options,
                               ILogger<RabbitMqPublisher> logger)
{
    private readonly RabbitMqOptions _options = options.Value;

    public async Task PublishAsync<TMessage>(
        TMessage message,
        string routingKey,
        CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password
        };
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);

        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(channelOptions, cancellationToken: cancellationToken);
        
        logger.LogInformation(
            "Declaring RabbitMQ exchange {ExchangeName}",
            _options.ExchangeName);

        await channel.ExchangeDeclareAsync(
            exchange: _options.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        var json = JsonSerializer.Serialize(message);
        var body = Encoding.UTF8.GetBytes(json);

        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json"
        };
        
        logger.LogInformation(
            "Publishing message {MessageType} to exchange {ExchangeName} with routing key {RoutingKey}",
            typeof(TMessage).Name,
            _options.ExchangeName,
            routingKey);

        await channel.BasicPublishAsync(
            exchange: _options.ExchangeName,
            routingKey: routingKey,
            mandatory: true,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
        
        logger.LogInformation(
            "Published message {MessageType} to exchange {ExchangeName} with routing key {RoutingKey}",
            typeof(TMessage).Name,
            _options.ExchangeName,
            routingKey);
    }

    public async Task PublishJsonAsync(
        string payload,
        string routingKey,
        Guid messageId,
        Guid correlationId,
        string messageType,
        CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password
        };

        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);

        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(
            channelOptions,
            cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            exchange: _options.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        var body = Encoding.UTF8.GetBytes(payload);

        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = messageId.ToString(),
            CorrelationId = correlationId.ToString(),
            Type = messageType,
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>
            {
                ["message-id"] = messageId.ToString(),
                ["correlation-id"] = correlationId.ToString(),
                ["message-type"] = messageType
            }
        };

        logger.LogInformation(
            "Publishing outbox message {MessageId} of type {MessageType} to exchange {ExchangeName} with routing key {RoutingKey}",
            messageId,
            correlationId,
            messageType,
            _options.ExchangeName,
            routingKey);

        await channel.BasicPublishAsync(
            exchange: _options.ExchangeName,
            routingKey: routingKey,
            mandatory: true,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Published outbox message {MessageId} of type {MessageType}",
            messageId,
            messageType);
    }
}
