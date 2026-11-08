using Microsoft.Extensions.Options;
using OperaHouse.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OperaHouse.Notification.Messaging;

public class Worker(ILogger<Worker> logger,
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory serviceScopeFactory,
    IConnectionFactory connectionFactory)
    : BackgroundService
{
    private readonly RabbitMqOptions _options = options.Value;
    private readonly NotificationDeliveryHandler _deliveries = new(logger, serviceScopeFactory, options.Value);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Notification RabbitMQ session failed. Reconnecting in five seconds.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    protected async Task RunSessionAsync(CancellationToken hostStoppingToken)
    {
        using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(hostStoppingToken);
        var stoppingToken = sessionCancellation.Token;
        var sessionFailure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        Task OnCallbackExceptionAsync(object sender, CallbackExceptionEventArgs args)
        {
            sessionFailure.TrySetResult(args.Exception);
            return sessionCancellation.CancelAsync();
        }

        Task OnShutdownAsync(object sender, ShutdownEventArgs args)
        {
            sessionFailure.TrySetResult(new IOException($"RabbitMQ session closed: {args.ReplyText}"));
            return sessionCancellation.CancelAsync();
        }

        await using var connection = await connectionFactory.CreateConnectionAsync(stoppingToken);
        connection.ConnectionShutdownAsync += OnShutdownAsync;
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        channel.CallbackExceptionAsync += OnCallbackExceptionAsync;
        channel.ChannelShutdownAsync += OnShutdownAsync;

        var retryOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);
        await using var retryChannel = await connection.CreateChannelAsync(retryOptions, stoppingToken);
        retryChannel.ChannelShutdownAsync += OnShutdownAsync;

        await NotificationTopology.DeclareAsync(channel, _options, stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false,
            cancellationToken: stoppingToken);

        var createdConsumer = new AsyncEventingBasicConsumer(channel);
        createdConsumer.ReceivedAsync += (_, delivery) =>
            _deliveries.HandleBookingCreatedAsync(channel, retryChannel, delivery, stoppingToken);
        await channel.BasicConsumeAsync(queue: _options.BookingCreatedQueueName, autoAck: false,
            consumer: createdConsumer, cancellationToken: stoppingToken);

        var expiredConsumer = new AsyncEventingBasicConsumer(channel);
        expiredConsumer.ReceivedAsync += (_, delivery) =>
            _deliveries.HandleBookingExpiredAsync(channel, retryChannel, delivery, stoppingToken);
        await channel.BasicConsumeAsync(queue: _options.BookingExpiredQueueName, autoAck: false,
            consumer: expiredConsumer, cancellationToken: stoppingToken);

        var cancelledConsumer = new AsyncEventingBasicConsumer(channel);
        cancelledConsumer.ReceivedAsync += (_, delivery) =>
            _deliveries.HandleBookingCancelledAsync(channel, retryChannel, delivery, stoppingToken);
        await channel.BasicConsumeAsync(queue: _options.BookingCancelledQueueName, autoAck: false,
            consumer: cancelledConsumer, cancellationToken: stoppingToken);

        logger.LogInformation("Notification consumers ready: {CreatedQueue}, {ExpiredQueue}, {CancelledQueue}.",
            _options.BookingCreatedQueueName, _options.BookingExpiredQueueName, _options.BookingCancelledQueueName);

        // Callback failures run outside this task; surface them to the reconnect loop.
        throw await sessionFailure.Task.WaitAsync(hostStoppingToken);
    }
}
