using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace OperaHouse.Messaging;

public class RabbitMqPublisher(IOptions<RabbitMqOptions> options)
{
    private readonly RabbitMqOptions _options = options.Value;

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

        await channel.BasicPublishAsync(
            exchange: _options.ExchangeName,
            routingKey: routingKey,
            mandatory: true,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);

    }
}
