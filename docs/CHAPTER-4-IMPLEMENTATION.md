# Chapter 4 — File-by-file implementation

This is the actual code to apply, organized in dependency order. It covers expiration consumption, reliable retry publication, failure classification, unpaid cancellation, DLQ replay, and automated checks.

**These source changes are a draft for you to apply. The application's source files have not been changed by preparing this guide.** Full-file replacements below include existing behavior so they can be copied without assembling missing methods. New files are marked as new.

Work through one group at a time. Keep the existing service consumer names: changing them would make old inbox records stop matching.

## Implementation order

1. Fix the SDK selection and local database connection.
2. Add the cancellation contract, queue options, and processing interface.
3. Add the email failure type and correct SMTP delivery.
4. Replace notification processing with the three explicit event entry points and shared persistence/send behavior.
5. Replace the worker with consumers for created, expired, and cancelled messages.
6. Add transactional unpaid cancellation and its admin-protected API operation.
7. Add the controlled single-message DLQ replay tool.
8. Add the tests and run the local verification steps.

The API and workers remain in their existing processes for Chapter 4. Postmark, Terraform, managed identity, and the separate Booking Worker host belong to Chapter 5.

## 1. Local baseline

Replace `global.json` with the following. The existing SDK version `10.0.0` is not a valid SDK feature-band version. This selection was checked with the installed .NET 10.0.402 SDK.

### global.json

Replace this file with the following.

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
```

In **both** Notification Worker settings files (`appsettings.json` and `appsettings.Development.json`), update the existing `ConnectionStrings:NotificationDatabase` value to match Docker. Preserve other settings and any environment-specific overrides:

```json
"ConnectionStrings": {
  "NotificationDatabase": "Host=127.0.0.1;Port=54400;Database=operahouse;Username=operahouse;Password=operahouse"
}
```

Those are the repository's local Docker credentials. SMTP credentials stay in User Secrets. Do not copy real credentials into the guide or committed settings.

## 2. Event contract and configuration

`BookingCreated` and `BookingExpired` already exist. Add only the cancellation contract, extend the existing queue options, and expose the cancellation service method.

### src/OperaHouse.Contracts/Events/BookingCancelled.cs

Create this file.

```csharp
namespace OperaHouse.Contracts.Events;

public sealed record BookingCancelled(
    Guid MessageId,
    Guid CorrelationId,
    Guid BookingId,
    Guid PerformanceId,
    string CustomerEmail,
    int Seats,
    DateTimeOffset OccurredAt);
```

### src/OperaHouse.Messaging/RabbitMqOptions.cs

Replace this file with the following.

```csharp
namespace OperaHouse.Messaging;

public class RabbitMqOptions
{
    public string HostName { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 5672;
    public string UserName { get; init; } = "guest";
    public string Password { get; init; } = "guest";
    public string ExchangeName { get; init; } = "operahouse.events";
    public string BookingCreatedRoutingKey { get; init; } = "booking.created";
    public string BookingCreatedQueueName { get; init; } = "notification-booking-created.queue";
    public string DeadLetterExchangeName { get; init; } = "operahouse.dead-letter";
    public string BookingCreatedDeadLetterQueueName { get; init; } = "notification-booking-created.dead-letter.queue";
    public string BookingCreatedDeadLetterRoutingKey { get; init; } = "booking.created.dead";
    public string RetryExchangeName { get; init; } = "operahouse.retry";
    public string BookingCreatedRetryQueueName { get; init; } = "notification-booking-created.retry.queue";
    public string BookingCreatedRetryRoutingKey { get; init; } = "booking.created.retry";
    public int BookingCreatedRetryDelayMilliseconds { get; init; } = 10000;
    public int BookingCreatedMaxRetryAttempts { get; init; } = 3;
    public string BookingExpiredRoutingKey { get; init; } = "booking.expired";
    public string BookingExpiredQueueName { get; init; } = "notification-booking-expired.queue";

    public string BookingExpiredRetryQueueName { get; init; } = "notification-booking-expired.retry.queue";

    public string BookingExpiredRetryRoutingKey { get; init; } = "booking.expired.retry";

    public int BookingExpiredRetryDelayMilliseconds { get; init; } = 10_000;

    public int BookingExpiredMaxRetryAttempts { get; init; } = 3;

    public string BookingExpiredDeadLetterQueueName { get; init; } = "notification-booking-expired.dead-letter.queue";

    public string BookingExpiredDeadLetterRoutingKey { get; init; } = "booking.expired.dead";

    public string BookingCancelledRoutingKey { get; init; } = "booking.cancelled";
    public string BookingCancelledQueueName { get; init; } = "notification-booking-cancelled.queue";
    public string BookingCancelledRetryQueueName { get; init; } = "notification-booking-cancelled.retry.queue";
    public string BookingCancelledRetryRoutingKey { get; init; } = "booking.cancelled.retry";
    public int BookingCancelledRetryDelayMilliseconds { get; init; } = 10_000;
    public int BookingCancelledMaxRetryAttempts { get; init; } = 3;
    public string BookingCancelledDeadLetterQueueName { get; init; } = "notification-booking-cancelled.dead-letter.queue";
    public string BookingCancelledDeadLetterRoutingKey { get; init; } = "booking.cancelled.dead";
}
```

### src/OperaHouse.Notification.Application/Notifications/INotificationService.cs

Replace this file with the following.

```csharp
using OperaHouse.Contracts.Events;

namespace OperaHouse.Notification.Application.Notifications;

public interface INotificationService
{
    Task<NotificationProcessingResult> ProcessBookingCreatedAsync(
        BookingCreated message,
        CancellationToken cancellationToken);

    Task<NotificationProcessingResult> ProcessBookingExpiredAsync(
        BookingExpired message,
        CancellationToken cancellationToken);

    Task<NotificationProcessingResult> ProcessBookingCancelledAsync(
        BookingCancelled message,
        CancellationToken cancellationToken);
}
```

## 3. SMTP delivery and failure classification

The Application layer owns a small provider-independent exception. The SMTP adapter translates provider errors into it. Invalid addresses and authentication failures are permanent; SMTP 4xx command failures are retryable. Network failures have an uncertain send outcome and are bounded by the worker's retry policy.

The adapter also fixes three existing problems: it authenticates with `UserName`, sends even when authentication is not required, and does not report a failed QUIT as a failed accepted send.

### src/OperaHouse.Notification.Application/Notifications/EmailDeliveryException.cs

Create this file.

```csharp
namespace OperaHouse.Notification.Application.Notifications;

public sealed class EmailDeliveryException(
    string message,
    bool isTransient,
    Exception? innerException = null) : Exception(message, innerException)
{
    public bool IsTransient { get; } = isTransient;
}
```

### src/OperaHouse.Notification.Infrastructure/Email/SmtpEmailSender.cs

Replace this file with the following.

```csharp
using System.Net.Sockets;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using OperaHouse.Notification.Application.Notifications;

namespace OperaHouse.Notification.Infrastructure.Email;

public sealed class SmtpEmailSender(
    IOptions<EmailOptions> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public async Task SendAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        if (!MailboxAddress.TryParse(recipient, out var recipientAddress)
            || !recipientAddress.Address.Contains('@'))
        {
            throw new EmailDeliveryException("Invalid recipient address.", false);
        }

        if (!MailboxAddress.TryParse(_options.FromAddress, out var senderAddress)
            || !senderAddress.Address.Contains('@'))
        {
            throw new EmailDeliveryException("Invalid sender configuration.", false);
        }

        senderAddress.Name = _options.FromName;
        var message = new MimeMessage();
        message.From.Add(senderAddress);
        message.To.Add(recipientAddress);
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient { Timeout = 15_000 };

        try
        {
            await client.ConnectAsync(
                _options.Host,
                _options.Port,
                _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(_options.UserName))
            {
                await client.AuthenticateAsync(
                    _options.UserName,
                    _options.Password,
                    cancellationToken);
            }

            // Sending is independent of whether this server requires authentication.
            await client.SendAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MailKit.Security.AuthenticationException exception)
        {
            throw new EmailDeliveryException("SMTP authentication failed.", false, exception);
        }
        catch (SmtpCommandException exception)
        {
            var statusCode = (int)exception.StatusCode;
            throw new EmailDeliveryException(
                $"SMTP command failed with status {statusCode}.",
                statusCode is >= 400 and < 500,
                exception);
        }
        catch (Exception exception) when (
            exception is IOException or SocketException or SmtpProtocolException
                or OperationCanceledException)
        {
            throw new EmailDeliveryException(
                "SMTP communication failed; the send outcome may be uncertain.",
                true,
                exception);
        }

        logger.LogInformation("SMTP server accepted the notification.");

        // A failed QUIT must not turn an accepted email into a failed send.
        try
        {
            await client.DisconnectAsync(true, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "SMTP disconnect failed after message acceptance.");
        }
    }
}
```

### src/OperaHouse.Notification.Worker/Program.cs

Replace this file with the following.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MimeKit;
using OperaHouse.Messaging;
using OperaHouse.Notification.Application.Inbox;
using OperaHouse.Notification.Application.Notifications;
using OperaHouse.Notification.Infrastructure.Email;
using OperaHouse.Notification.Infrastructure.Inbox;
using OperaHouse.Notification.Infrastructure.Notifications;
using OperaHouse.Notification.Infrastructure.HealthChecks;
using OperaHouse.Notification.Infrastructure.Persistence;
using OperaHouse.Notification.Worker;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("NotificationDatabase")
    ?? throw new InvalidOperationException("Connection string 'NotificationDatabase' was not found.");

builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection("RabbitMq"));

builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddOptions<EmailOptions>()
    .Bind(builder.Configuration.GetSection(EmailOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.Host),
        "Email: Host is required.")
    .Validate(
        options => options.Port is > 0 and <= 65535,
        "Email: Port must be a valid TCP port."
    )
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.FromAddress),
        "Email: FromAdress is required."
    )
    .Validate(
        options => MailboxAddress.TryParse(
            options.FromAddress,
            out _),
        "Email: FromAddress must be a valid email address."
    )
    .Validate(
        options => string.IsNullOrWhiteSpace(options.UserName)
            == string.IsNullOrWhiteSpace(options.Password),
        "Configure both Email:UserName and Email:Password, or neither.")
    .ValidateOnStart();

builder.Services.AddScoped<IInboxRepository, InboxRepository>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<INotificationUnitOfWork, NotificationUnitOfWork>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddHealthChecks()
    .AddCheck<NotificationDatabaseHealthCheck>("notification-database")
    .AddCheck<RabbitMqHealthCheck>("rabbitmq");
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<HealthLoggingWorker>();

var host = builder.Build();
host.Run();
```

## 4. Notification processing

The three public methods describe each email explicitly. The private method shares the inbox lookup, notification persistence, sending, and result recording. It persists an attempt before making the external call.

This retains completed-message deduplication. It does not provide a multi-worker processing lease or exactly-once SMTP sending. A successful send followed by a failed database save remains uncertain and is tested below. Keep one Notification Worker instance for this chapter; concurrent processing protection is required before scaling out in Chapter 5.

### src/OperaHouse.Notification.Application/Notifications/NotificationService.cs

Replace this file with the following.

```csharp
using Microsoft.Extensions.Logging;
using OperaHouse.Contracts.Events;
using OperaHouse.Notification.Application.Inbox;
using OperaHouse.Notification.Domain.Inbox;
using OperaHouse.Notification.Domain.Notifications;

namespace OperaHouse.Notification.Application.Notifications;

public sealed class NotificationService(
    IInboxRepository inboxRepository,
    INotificationRepository notificationRepository,
    IEmailSender emailSender,
    INotificationUnitOfWork unitOfWork,
    ILogger<NotificationService> logger) : INotificationService
{
    // Preserve existing names so previously processed messages remain recognizable.
    private const string CreatedConsumer = "OperaHouse.Notification.Worker";
    private const string ExpiredConsumer = "OperaHouse.Notification.BookingExpired";
    private const string CancelledConsumer = "OperaHouse.Notification.BookingCancelled";

    public Task<NotificationProcessingResult> ProcessBookingCreatedAsync(
        BookingCreated message,
        CancellationToken cancellationToken) => ProcessAsync(
            message.MessageId, CreatedConsumer, nameof(BookingCreated),
            message.CustomerEmail, "BookingReceived", "Booking received",
            $"Your reservation {message.BookingId} was received. " +
            $"It expires at {message.ExpiresAt:O} unless confirmed.",
            cancellationToken);

    public Task<NotificationProcessingResult> ProcessBookingExpiredAsync(
        BookingExpired message,
        CancellationToken cancellationToken) => ProcessAsync(
            message.MessageId, ExpiredConsumer, nameof(BookingExpired),
            message.CustomerEmail, "BookingExpiration", "Your OperaHouse reservation expired",
            $"Your reservation {message.BookingId} expired. " +
            $"The {message.Seats} reserved seat(s) were released.",
            cancellationToken);

    public Task<NotificationProcessingResult> ProcessBookingCancelledAsync(
        BookingCancelled message,
        CancellationToken cancellationToken) => ProcessAsync(
            message.MessageId, CancelledConsumer, nameof(BookingCancelled),
            message.CustomerEmail, "BookingCancellation", "Your OperaHouse reservation was cancelled",
            $"Your reservation {message.BookingId} was cancelled. " +
            $"The {message.Seats} reserved seat(s) were released.",
            cancellationToken);

    private async Task<NotificationProcessingResult> ProcessAsync(
        Guid messageId,
        string consumer,
        string messageType,
        string recipient,
        string notificationType,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        var inbox = await inboxRepository.GetByMessageAsync(messageId, consumer, cancellationToken);

        if (inbox?.Status == InboxMessageStatus.Processed)
        {
            logger.LogInformation("Message {MessageId} was already processed by {Consumer}.",
                messageId, consumer);
            return NotificationProcessingResult.Duplicate;
        }

        if (inbox is null)
        {
            inbox = new InboxMessage
            {
                Id = Guid.NewGuid(), MessageId = messageId,
                MessageType = messageType, Consumer = consumer,
                ReceivedAt = DateTimeOffset.UtcNow
            };
            await inboxRepository.AddAsync(inbox, cancellationToken);
        }

        inbox.Status = InboxMessageStatus.Processing;
        inbox.LastError = null;

        var notification = await notificationRepository.GetByInboxMessageIdAsync(
            inbox.Id, cancellationToken);

        if (notification is null)
        {
            notification = new NotificationMessage
            {
                Id = Guid.NewGuid(), InboxMessageId = inbox.Id,
                Type = notificationType, Recipient = recipient,
                Subject = subject, Body = body,
                CreatedAt = DateTimeOffset.UtcNow
            };
            await notificationRepository.AddAsync(notification, cancellationToken);
        }

        notification.Status = NotificationStatus.Pending;
        notification.FailureReason = null;
        notification.FailedAt = null;

        // Persist the attempt before making the external call.
        // This does not make SMTP delivery exactly once or provide a multi-worker lease.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            await emailSender.SendAsync(notification.Recipient, notification.Subject,
                notification.Body, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var failure = exception.Message.Length <= 2_000
                ? exception.Message : exception.Message[..2_000];
            notification.Status = NotificationStatus.Failed;
            notification.FailedAt = DateTimeOffset.UtcNow;
            notification.FailureReason = failure;
            inbox.Status = InboxMessageStatus.Failed;
            inbox.LastError = failure;
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw;
        }

        notification.Status = NotificationStatus.Sent;
        notification.SentAt = DateTimeOffset.UtcNow;
        inbox.Status = InboxMessageStatus.Processed;
        inbox.ProcessedAt = DateTimeOffset.UtcNow;
        inbox.LastError = null;

        // An error here means the email may be sent but its outcome is not recorded.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return NotificationProcessingResult.Processed;
    }
}
```

## 5. Consumers, retry publication, and transport recovery

The worker has three event-specific processing methods. A small private queue-settings record groups the fields needed to declare each event's main/retry/dead-letter queues.

A separate publisher-confirm channel handles retry publication. The original delivery is ACKed only after confirmed, routable retry publication. ACK failures are outside the business-processing catch. Transport failures recreate the session so unacknowledged deliveries are not left blocking prefetch indefinitely. Channel callbacks run serially in this chapter.

Declare and start this worker's topology before enabling new event producers. The outbox publisher remains mandatory/confirmed so a missing route leaves the outbox record recoverable.

### src/OperaHouse.Notification.Worker/Worker.cs

Replace this file with the following.

```csharp
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OperaHouse.Contracts.Events;
using OperaHouse.Messaging;
using OperaHouse.Notification.Application.Notifications;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OperaHouse.Notification.Worker;

public sealed class Worker(
    ILogger<Worker> logger,
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory scopeFactory) : BackgroundService
{
    private readonly RabbitMqOptions _options = options.Value;

    // These are queue settings, not a new messaging framework.
    private sealed record QueueSettings(
        string Queue, string RoutingKey, string RetryQueue, string RetryRoutingKey,
        string DeadLetterQueue, string DeadLetterRoutingKey,
        int DelayMilliseconds, int MaxRetries);

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
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Notification connection/session failed; reconnecting.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RunSessionAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName, Port = _options.Port,
            UserName = _options.UserName, Password = _options.Password,
            ConsumerDispatchConcurrency = 1,
            // Recreate the complete session explicitly after transport failures.
            AutomaticRecoveryEnabled = false,
            ClientProvidedName = "operahouse-notifications"
        };

        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var consumerChannel = await connection.CreateChannelAsync(
            cancellationToken: stoppingToken);
        await using var retryChannel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            stoppingToken);

        foreach (var exchange in new[]
        {
            _options.ExchangeName, _options.RetryExchangeName, _options.DeadLetterExchangeName
        })
        {
            await consumerChannel.ExchangeDeclareAsync(exchange, ExchangeType.Topic,
                durable: true, autoDelete: false, cancellationToken: stoppingToken);
        }

        (QueueSettings Settings,
            Func<byte[], CancellationToken, Task<NotificationProcessingResult>> Process)[] subscriptions =
        [
            (new(_options.BookingCreatedQueueName, _options.BookingCreatedRoutingKey,
                _options.BookingCreatedRetryQueueName, _options.BookingCreatedRetryRoutingKey,
                _options.BookingCreatedDeadLetterQueueName, _options.BookingCreatedDeadLetterRoutingKey,
                _options.BookingCreatedRetryDelayMilliseconds, _options.BookingCreatedMaxRetryAttempts),
                ProcessCreatedAsync),
            (new(_options.BookingExpiredQueueName, _options.BookingExpiredRoutingKey,
                _options.BookingExpiredRetryQueueName, _options.BookingExpiredRetryRoutingKey,
                _options.BookingExpiredDeadLetterQueueName, _options.BookingExpiredDeadLetterRoutingKey,
                _options.BookingExpiredRetryDelayMilliseconds, _options.BookingExpiredMaxRetryAttempts),
                ProcessExpiredAsync),
            (new(_options.BookingCancelledQueueName, _options.BookingCancelledRoutingKey,
                _options.BookingCancelledRetryQueueName, _options.BookingCancelledRetryRoutingKey,
                _options.BookingCancelledDeadLetterQueueName, _options.BookingCancelledDeadLetterRoutingKey,
                _options.BookingCancelledRetryDelayMilliseconds, _options.BookingCancelledMaxRetryAttempts),
                ProcessCancelledAsync)
        ];

        foreach (var subscription in subscriptions)
        {
            await DeclareTopologyAsync(consumerChannel, subscription.Settings, stoppingToken);
        }

        await consumerChannel.BasicQosAsync(0, 1, false, stoppingToken);
        var sessionFailure = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        connection.ConnectionShutdownAsync += (_, reason) =>
        {
            sessionFailure.TrySetResult(new IOException($"Connection shutdown: {reason.ReplyCode}."));
            return Task.CompletedTask;
        };
        consumerChannel.ChannelShutdownAsync += (_, reason) =>
        {
            sessionFailure.TrySetResult(new IOException($"Consumer channel shutdown: {reason.ReplyCode}."));
            return Task.CompletedTask;
        };
        retryChannel.ChannelShutdownAsync += (_, reason) =>
        {
            sessionFailure.TrySetResult(new IOException($"Retry channel shutdown: {reason.ReplyCode}."));
            return Task.CompletedTask;
        };

        foreach (var subscription in subscriptions)
        {
            var consumer = new AsyncEventingBasicConsumer(consumerChannel);
            consumer.ReceivedAsync += async (_, delivery) =>
            {
                try
                {
                    var body = delivery.Body.ToArray();
                    await ProcessDeliveryAsync(consumerChannel, retryChannel, delivery, body,
                        subscription.Settings, subscription.Process, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Session disposal requeues deliveries that remain unacknowledged.
                }
                catch (Exception exception)
                {
                    // Includes ACK failures and failed retry publication. Recreate channels
                    // instead of leaving prefetch blocked by an unacknowledged delivery.
                    sessionFailure.TrySetResult(exception);
                }
            };

            await consumerChannel.BasicConsumeAsync(subscription.Settings.Queue,
                autoAck: false, consumer: consumer, cancellationToken: stoppingToken);
            logger.LogInformation("Consuming {Queue}.", subscription.Settings.Queue);
        }

        var failure = await sessionFailure.Task.WaitAsync(stoppingToken);
        throw new InvalidOperationException("Notification session must be recreated.", failure);
    }

    private async Task ProcessDeliveryAsync(
        IChannel consumerChannel,
        IChannel retryChannel,
        BasicDeliverEventArgs delivery,
        byte[] body,
        QueueSettings settings,
        Func<byte[], CancellationToken, Task<NotificationProcessingResult>> process,
        CancellationToken cancellationToken)
    {
        using var loggingScope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["MessageId"] = delivery.BasicProperties.MessageId,
            ["CorrelationId"] = delivery.BasicProperties.CorrelationId,
            ["RoutingKey"] = delivery.RoutingKey
        });

        var retries = 0;
        NotificationProcessingResult result;
        try
        {
            retries = GetRetryCount(delivery.BasicProperties);
            result = await process(body, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var permanent = exception is JsonException or InvalidDataException
                or EmailDeliveryException { IsTransient: false };

            logger.LogWarning(exception,
                "Notification processing failed. Permanent: {Permanent}; attempt: {Attempt}.",
                permanent, retries);

            if (permanent || retries >= settings.MaxRetries)
            {
                await consumerChannel.BasicNackAsync(delivery.DeliveryTag,
                    multiple: false, requeue: false, cancellationToken: cancellationToken);
                logger.LogWarning("Rejected to {DeadLetterQueue}.", settings.DeadLetterQueue);
                return;
            }

            // This throws on a negative confirmation, unroutable publish, or timeout.
            // The callback then recreates the session without ACKing this delivery.
            await PublishRetryAsync(retryChannel, delivery, body, settings,
                retries + 1, exception, cancellationToken);
            await consumerChannel.BasicAckAsync(delivery.DeliveryTag, false, cancellationToken);
            return;
        }

        // Keep ACK errors outside the processing catch: do not turn an ACK failure
        // into a new email attempt or a business retry.
        await consumerChannel.BasicAckAsync(delivery.DeliveryTag, false, cancellationToken);
        logger.LogInformation("Notification completed: {Result}.", result);
    }

    private async Task PublishRetryAsync(
        IChannel channel,
        BasicDeliverEventArgs delivery,
        byte[] body,
        QueueSettings settings,
        int retryCount,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var headers = delivery.BasicProperties.Headers?.ToDictionary(x => x.Key, x => x.Value)
            ?? new Dictionary<string, object?>();
        var now = DateTimeOffset.UtcNow.ToString("O");
        headers.TryAdd("x-original-exchange", delivery.Exchange);
        headers.TryAdd("x-original-routing-key", delivery.RoutingKey);
        headers.TryAdd("x-first-failed-at", now);
        headers["x-retry-count"] = retryCount;
        headers["x-last-failed-at"] = now;
        headers["x-last-error"] = exception.GetType().Name;

        var properties = new BasicProperties
        {
            Persistent = true, ContentType = delivery.BasicProperties.ContentType ?? "application/json",
            MessageId = delivery.BasicProperties.MessageId,
            CorrelationId = delivery.BasicProperties.CorrelationId,
            Type = delivery.BasicProperties.Type,
            Timestamp = delivery.BasicProperties.Timestamp,
            Headers = headers
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await channel.BasicPublishAsync(_options.RetryExchangeName,
            settings.RetryRoutingKey, mandatory: true, basicProperties: properties,
            body: body, cancellationToken: timeout.Token);
        logger.LogWarning("Published confirmed retry {RetryCount} to {RetryQueue}.",
            retryCount, settings.RetryQueue);
    }

    private async Task DeclareTopologyAsync(
        IChannel channel, QueueSettings settings, CancellationToken cancellationToken)
    {
        await channel.QueueDeclareAsync(settings.DeadLetterQueue, true, false, false,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(settings.DeadLetterQueue, _options.DeadLetterExchangeName,
            settings.DeadLetterRoutingKey, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(settings.Queue, true, false, false,
            new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = _options.DeadLetterExchangeName,
                ["x-dead-letter-routing-key"] = settings.DeadLetterRoutingKey
            }, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(settings.Queue, _options.ExchangeName,
            settings.RoutingKey, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(settings.RetryQueue, true, false, false,
            new Dictionary<string, object?>
            {
                ["x-message-ttl"] = settings.DelayMilliseconds,
                ["x-dead-letter-exchange"] = _options.ExchangeName,
                ["x-dead-letter-routing-key"] = settings.RoutingKey
            }, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(settings.RetryQueue, _options.RetryExchangeName,
            settings.RetryRoutingKey, cancellationToken: cancellationToken);
    }

    private async Task<NotificationProcessingResult> ProcessCreatedAsync(byte[] body, CancellationToken token)
    {
        var message = JsonSerializer.Deserialize<BookingCreated>(body)
            ?? throw new InvalidDataException("Missing BookingCreated payload.");
        Validate(message.MessageId, message.CorrelationId, message.BookingId,
            message.PerformanceId, message.CustomerEmail, message.Seats);
        if (message.ExpiresAt <= message.OccurredAt)
            throw new InvalidDataException("Invalid reservation deadline.");
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<INotificationService>()
            .ProcessBookingCreatedAsync(message, token);
    }

    private async Task<NotificationProcessingResult> ProcessExpiredAsync(byte[] body, CancellationToken token)
    {
        var message = JsonSerializer.Deserialize<BookingExpired>(body)
            ?? throw new InvalidDataException("Missing BookingExpired payload.");
        Validate(message.MessageId, message.CorrelationId, message.BookingId,
            message.PerformanceId, message.CustomerEmail, message.Seats);
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<INotificationService>()
            .ProcessBookingExpiredAsync(message, token);
    }

    private async Task<NotificationProcessingResult> ProcessCancelledAsync(byte[] body, CancellationToken token)
    {
        var message = JsonSerializer.Deserialize<BookingCancelled>(body)
            ?? throw new InvalidDataException("Missing BookingCancelled payload.");
        Validate(message.MessageId, message.CorrelationId, message.BookingId,
            message.PerformanceId, message.CustomerEmail, message.Seats);
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<INotificationService>()
            .ProcessBookingCancelledAsync(message, token);
    }

    private static void Validate(Guid messageId, Guid correlationId, Guid bookingId,
        Guid performanceId, string email, int seats)
    {
        if (messageId == Guid.Empty || correlationId == Guid.Empty || bookingId == Guid.Empty
            || performanceId == Guid.Empty || string.IsNullOrWhiteSpace(email) || seats <= 0)
            throw new InvalidDataException("Required notification event fields are invalid.");
    }

    private static int GetRetryCount(IReadOnlyBasicProperties properties)
    {
        if (properties.Headers is null || !properties.Headers.TryGetValue("x-retry-count", out var value))
            return 0;

        return value switch
        {
            int number when number >= 0 => number,
            long number when number is >= 0 and <= int.MaxValue => (int)number,
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed)
                && parsed >= 0 => parsed,
            _ => throw new InvalidDataException("Invalid retry count header.")
        };
    }
}
```

## 6. Transactional unpaid cancellation

**Access choice for Chapter 4:** the cancellation endpoint uses the existing Admin authorization policy. Guest cancellation will require scoped access credentials in Chapter 5; a public booking ID alone never authorizes this operation.

A pending reservation before its deadline can be cancelled. Repeated cancellation returns the existing cancelled reservation. Confirmed/expired reservations and pending reservations whose deadline has passed return a conflict. Deadline-passed pending reservations are handled by expiration.

The repository conditionally changes `Pending` to `Cancelled`, releases seats, and inserts `BookingCancelled` in one database transaction. The conditional update coordinates competing cancellation/expiration operations.

No new schema migration is needed for these additions: cancellation fields, status strings, inbox records, and outbox storage already exist. Apply and verify the existing migrations.

The repository replacement also fixes the same-idempotency-key/last-seat race: if another request already used the key while the conditional seat update waited, load and return that reservation rather than incorrectly reporting that the performance is unavailable.

### src/OperaHouse.Booking.Application/Bookings/BookingCancellationOutcome.cs

Create this file.

```csharp
namespace OperaHouse.Booking.Application.Bookings;

public enum BookingCancellationOutcome
{
    Cancelled,
    AlreadyCancelled,
    NotFound,
    NotCancellable
}
```

### src/OperaHouse.Booking.Application/Bookings/IBookingRepository.cs

Replace this file with the following.

```csharp
using OperaHouse.Contracts.Events;
using BookingEntity = OperaHouse.Booking.Domain.Bookings.Booking;

namespace OperaHouse.Booking.Application.Bookings;

public interface IBookingRepository
{
    Task<BookingEntity?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<BookingCreationPersistenceResult> TryCreateAsync(
        BookingEntity booking,
        BookingCreated bookingCreated,
        DateTimeOffset currentTime,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ExpiringBooking>> GetExpiringBookingAsync(
        DateTimeOffset currentTime,
        int batchSize,
        CancellationToken cancellationToken);

    Task<bool> TryExpireAsync(
        ExpiringBooking booking,
        BookingExpired bookingExpired,
        DateTimeOffset currentTime,
        CancellationToken cancellationToken);

    Task<BookingCancellationOutcome> TryCancelAsync(
        Guid bookingId,
        Guid correlationId,
        DateTimeOffset currentTime,
        CancellationToken cancellationToken);
}
```

### src/OperaHouse.Booking.Infrastructure/Bookings/BookingRepository.cs

Replace this file with the following.

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using OperaHouse.Booking.Application.Bookings;
using OperaHouse.Booking.Domain.Bookings;
using OperaHouse.Booking.Domain.Performances;
using OperaHouse.Booking.Infrastructure.Persistence;
using OperaHouse.Booking.Infrastructure.Persistence.Outbox;
using OperaHouse.Contracts.Events;
using OperaHouse.Messaging;
using BookingEntity = OperaHouse.Booking.Domain.Bookings.Booking;

namespace OperaHouse.Booking.Infrastructure.Bookings;

public sealed class BookingRepository(
    BookingDbContext dbContext,
    IOptions<RabbitMqOptions> rabbitMqOptions) : IBookingRepository
{
    private const string IdempotencyConstraintName = "UX_Bookings_IdempotencyKey";
    private readonly RabbitMqOptions _rabbitMqOptions = rabbitMqOptions.Value;
    public async Task<BookingEntity?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return await dbContext.Bookings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                booking => booking.Id == id,
                cancellationToken);
    }

    public async Task<BookingCreationPersistenceResult> TryCreateAsync(
        BookingEntity booking,
        BookingCreated bookingCreated,
        DateTimeOffset currentTime,
        CancellationToken cancellationToken)
    {
        var existingBooking = await FindByIdempotencyKeyAsync(
            booking.IdempotencyKey,
            cancellationToken);

        if (existingBooking is not null)
        {
            return CreateIdempotencyResult(existingBooking, booking);
        }

        await using var transaction = await dbContext
            .Database
            .BeginTransactionAsync(cancellationToken);

        try
        {
            var updatedRows = await dbContext.Performances
                .Where(performance =>
                    performance.Id == booking.PerformanceId
                    && performance.Status
                    == PerformanceStatus.Published
                    && performance.StartsAt > currentTime
                    && performance.AvailableSeats >= booking.Seats)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            performance =>
                                performance.AvailableSeats,
                            performance =>
                                performance.AvailableSeats - booking.Seats)
                        .SetProperty(performance =>
                            performance.UpdatedAt, currentTime),
                    cancellationToken);

            if (updatedRows == 0)
            {
                await transaction.RollbackAsync(cancellationToken);

                // A concurrent request with the same key may have reserved the last seats.
                var concurrent = await FindByIdempotencyKeyAsync(booking.IdempotencyKey, cancellationToken);
                return concurrent is not null
                    ? CreateIdempotencyResult(concurrent, booking)
                    : new BookingCreationPersistenceResult(BookingCreationOutcome.PerformanceUnavailable);
            }

            await dbContext.Bookings.AddAsync(booking, cancellationToken);

            await dbContext.OutboxMessages.AddAsync(
                CreateOutboxMessage(
                    bookingCreated.MessageId,
                    bookingCreated.CorrelationId,
                    nameof(BookingCreated),
                    _rabbitMqOptions.BookingCreatedRoutingKey,
                    bookingCreated.OccurredAt,
                    bookingCreated),
                cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new BookingCreationPersistenceResult(
                BookingCreationOutcome.Created,
                booking);
        }
        catch (DbUpdateException e)
        when(IsIdempotencyConflict(e))
        {
            await transaction.RollbackAsync(cancellationToken);
            
            dbContext.ChangeTracker.Clear();

            var concurrentBooking =
                await FindByIdempotencyKeyAsync(
                    booking.IdempotencyKey,
                    cancellationToken);

            if (concurrentBooking is null)
            {
                throw new InvalidOperationException(
                    "An idempotency conflict ocurred, but the existing booking could not be loaded",
                    e);
            }
            
            return CreateIdempotencyResult(
                concurrentBooking,
                booking);
        }
    }

    public async Task<IReadOnlyList<ExpiringBooking>> GetExpiringBookingAsync(
        DateTimeOffset currentTime,
        int batchSize,
        CancellationToken cancellationToken)
    {
        return await dbContext.Bookings
            .AsNoTracking()
            .Where(booking =>
                booking.Status == BookingStatus.Pending
                && booking.ExpiresAt <= currentTime)
            .OrderBy(booking => booking.ExpiresAt)
            .Take(batchSize)
            .Select(booking => new ExpiringBooking(
                booking.Id,
                booking.PerformanceId,
                booking.CustomerEmail,
                booking.Seats))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryExpireAsync(ExpiringBooking booking,
        BookingExpired bookingExpired,
        DateTimeOffset currentTime,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var expiredRows = await dbContext.Bookings
            .Where(existingBooking =>
                existingBooking.Id == booking.BookingId
                && existingBooking.Status == BookingStatus.Pending
                && existingBooking.ExpiresAt <= currentTime)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        existingBooking => existingBooking.Status,
                        BookingStatus.Expired)
                    .SetProperty(
                        existingBooking => existingBooking.ExpiredAt,
                        currentTime),
                cancellationToken);

        if (expiredRows == 0)
        {
            await transaction.RollbackAsync(cancellationToken);

            return false;
        }

        var releasedPerformanceRows =
            await dbContext.Performances
                .Where(performance =>
                    performance.Id == booking.PerformanceId
                    && performance.AvailableSeats
                    + booking.Seats
                    <= performance.Capacity)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(
                        performance =>
                            performance.AvailableSeats,
                        performance =>
                            performance.AvailableSeats + booking.Seats)
                    .SetProperty(
                        performance => performance.UpdatedAt,
                        currentTime), cancellationToken);

        if (releasedPerformanceRows != 1)
        {
            throw new InvalidOperationException($"Could not release seats for booking {booking.BookingId}");
        }

        await dbContext.OutboxMessages.AddAsync(
            CreateOutboxMessage(
                bookingExpired.MessageId,
                bookingExpired.CorrelationId,
                nameof(BookingExpired),
                _rabbitMqOptions.BookingExpiredRoutingKey,
                bookingExpired.OccurredAt,
                bookingExpired),
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    public async Task<BookingCancellationOutcome> TryCancelAsync(
        Guid bookingId,
        Guid correlationId,
        DateTimeOffset currentTime,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var booking = await GetByIdAsync(bookingId, cancellationToken);

        if (booking is null)
            return BookingCancellationOutcome.NotFound;
        if (booking.Status == BookingStatus.Cancelled)
            return BookingCancellationOutcome.AlreadyCancelled;
        if (booking.Status != BookingStatus.Pending || booking.ExpiresAt <= currentTime)
            return BookingCancellationOutcome.NotCancellable;

        var changed = await dbContext.Bookings
            .Where(existing => existing.Id == bookingId
                && existing.Status == BookingStatus.Pending
                && existing.ExpiresAt > currentTime)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(existing => existing.Status, BookingStatus.Cancelled)
                .SetProperty(existing => existing.CancelledAt, currentTime), cancellationToken);

        if (changed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            var winner = await GetByIdAsync(bookingId, cancellationToken);
            return winner?.Status == BookingStatus.Cancelled
                ? BookingCancellationOutcome.AlreadyCancelled
                : BookingCancellationOutcome.NotCancellable;
        }

        var released = await dbContext.Performances
            .Where(performance => performance.Id == booking.PerformanceId
                && performance.AvailableSeats + booking.Seats <= performance.Capacity)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(performance => performance.AvailableSeats,
                    performance => performance.AvailableSeats + booking.Seats)
                .SetProperty(performance => performance.UpdatedAt, currentTime), cancellationToken);

        if (released != 1)
            throw new InvalidOperationException($"Could not release seats for booking {bookingId}.");

        var message = new BookingCancelled(Guid.NewGuid(), correlationId,
            booking.Id, booking.PerformanceId, booking.CustomerEmail, booking.Seats, currentTime);
        await dbContext.OutboxMessages.AddAsync(CreateOutboxMessage(
            message.MessageId, message.CorrelationId, nameof(BookingCancelled),
            _rabbitMqOptions.BookingCancelledRoutingKey, message.OccurredAt, message), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return BookingCancellationOutcome.Cancelled;
    }

    private async Task<BookingEntity?> FindByIdempotencyKeyAsync(
        Guid idempotencyKey,
        CancellationToken cancellationToken)
    {
        return await dbContext.Bookings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                booking => booking.IdempotencyKey == idempotencyKey,
                cancellationToken);
    }

    private static BookingCreationPersistenceResult CreateIdempotencyResult(
        BookingEntity existingBooking,
        BookingEntity requestedBooking)
    {
        var representsSameRequest =
            existingBooking.HasSameReservationDetails(
                requestedBooking.PerformanceId,
                requestedBooking.CustomerEmail,
                requestedBooking.Seats);

        return representsSameRequest
            ? new BookingCreationPersistenceResult(
                BookingCreationOutcome.Existing, existingBooking)
            : new BookingCreationPersistenceResult(BookingCreationOutcome.IdempotencyConflict);
    }

    private static bool IsIdempotencyConflict(
        DbUpdateException exception)
    {
        return exception.InnerException
            is PostgresException
            {
                SqlState:
                    PostgresErrorCodes.UniqueViolation,
                ConstraintName:
                    IdempotencyConstraintName
            };
    }

    private static OutboxMessage CreateOutboxMessage<TMessage>(
        Guid messageId,
        Guid correlationId,
        string type,
        string routingKey,
        DateTimeOffset ocurredAt,
        TMessage message)
    {
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            MessageId = messageId,
            CorrelationId = correlationId,
            Type = type,
            RoutingKey = routingKey,
            Payload = JsonSerializer.Serialize(message),
            OccurredAt = ocurredAt
        };
    }
}
```

### src/OperaHouse.Booking.Application/Bookings/IBookingService.cs

Replace this file with the following.

```csharp
using OperaHouse.Booking.Application.Common;

namespace OperaHouse.Booking.Application.Bookings;

public interface IBookingService
{
    Task<ApplicationResult<BookingDto>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<ApplicationResult<BookingDto>> CreateAsync(
        CreateBookingInput input,
        CancellationToken cancellationToken);

    Task<ApplicationResult<BookingDto>> CancelAsync(
        Guid bookingId,
        CancellationToken cancellationToken);
}
```

### src/OperaHouse.Booking.Application/Bookings/BookingService.cs

Replace this file with the following.

```csharp
using AutoMapper;
using FluentValidation;
using Microsoft.Extensions.Options;
using OperaHouse.Booking.Application.Common;
using OperaHouse.Booking.Application.Performances;
using OperaHouse.Booking.Domain.Bookings;
using OperaHouse.Contracts.Events;
using BookingEntity = OperaHouse.Booking.Domain.Bookings.Booking;

namespace OperaHouse.Booking.Application.Bookings;

public sealed class BookingService(
    IBookingRepository bookingRepository,
    IValidator<CreateBookingInput> inputValidator,
    IOptions<BookingOptions> bookingOptions,
    TimeProvider timeProvider,
    IMapper mapper)
    : IBookingService
{
    public async Task<ApplicationResult<BookingDto>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var booking = await bookingRepository.GetByIdAsync(
            id,
            cancellationToken);

        if (booking is null)
        {
            return ApplicationResult<BookingDto>.Failure(
                new ApplicationError(
                    "booking.not-found",
                    "Booking was not found.",
                    ApplicationErrorType.NotFound));
        }

        return ApplicationResult<BookingDto>.Success(
            mapper.Map<BookingDto>(booking));
    }

    public async Task<ApplicationResult<BookingDto>> CreateAsync(
        CreateBookingInput input,
        CancellationToken cancellationToken)
    {
        var validationResult = await inputValidator.ValidateAsync(
            input,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return ApplicationResult<BookingDto>.Failure(
                validationResult.ToApplicationError(
                    "booking.validation",
                    "The booking request is invalid."));
        }

        var currentTime = timeProvider.GetUtcNow();
        var expiresAt = currentTime
            .AddMinutes(bookingOptions
                .Value
                .ReservationDurationMinutes);

        var booking = new BookingEntity(
            id: Guid.NewGuid(),
            idempotencyKey: input.IdempotencyKey,
            performanceId: input.PerformanceId,
            customerEmail: input.CustomerEmail,
            seats: input.Seats,
            createdAt: currentTime,
            expiresAt: expiresAt);
        
        var bookingCreated = new BookingCreated(
            MessageId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            BookingId: booking.Id,
            PerformanceId: booking.PerformanceId,
            CustomerEmail: booking.CustomerEmail,
            Seats: booking.Seats,
            ExpiresAt: booking.ExpiresAt,
            OccurredAt: currentTime);

        var persistenceResult = await bookingRepository.TryCreateAsync(
            booking,
            bookingCreated,
            currentTime,
            cancellationToken);

        return persistenceResult.Outcome switch
        {
            BookingCreationOutcome.Created
                or BookingCreationOutcome.Existing =>
                ApplicationResult<BookingDto>.Success(
                    mapper.Map<BookingDto>(persistenceResult.Booking!)),

            BookingCreationOutcome.PerformanceUnavailable =>
                ApplicationResult<BookingDto>.Failure(
                    new ApplicationError(
                        "performance.unavailable",
                        "the performance is unavailable or does not have enough available seats.",
                        ApplicationErrorType.Conflict
                    )),

            BookingCreationOutcome.IdempotencyConflict =>
                ApplicationResult<BookingDto>.Failure(
                    new ApplicationError(
                        "booking.idempotency-conflict",
                        "The Idempotency-Key was already used for a different booking request.",
                        ApplicationErrorType.Conflict)),

            _ => throw new InvalidOperationException("Unknown booking creation outcome.")
        };
    }

    public async Task<ApplicationResult<BookingDto>> CancelAsync(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        var outcome = await bookingRepository.TryCancelAsync(
            bookingId, Guid.NewGuid(), timeProvider.GetUtcNow(), cancellationToken);

        if (outcome == BookingCancellationOutcome.NotFound)
            return ApplicationResult<BookingDto>.Failure(new ApplicationError(
                "booking.not-found", "Booking was not found.", ApplicationErrorType.NotFound));

        if (outcome == BookingCancellationOutcome.NotCancellable)
            return ApplicationResult<BookingDto>.Failure(new ApplicationError(
                "booking.not-cancellable",
                "Only a pending reservation before its deadline can be cancelled.",
                ApplicationErrorType.Conflict));

        return await GetByIdAsync(bookingId, cancellationToken);
    }
}
```

### src/OperaHouse.Booking.Api/Controllers/BookingsController.cs

Replace this file with the following.

```csharp
using Microsoft.AspNetCore.Authorization;
using OperaHouse.Booking.Api.Security;
using Microsoft.AspNetCore.Mvc;
using OperaHouse.Booking.Api.Errors;
using OperaHouse.Booking.Api.Models.Bookings;
using OperaHouse.Booking.Application.Bookings;

namespace OperaHouse.Booking.Api.Controllers;

[ApiController]
[Route("bookings")]
public sealed class BookingsController(IBookingService bookingService) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BookingDto>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await bookingService.GetByIdAsync(
            id,
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : this.ToActionResult(result.Error!);
    }

    [HttpPost]
    public async Task<ActionResult<BookingDto>> Create(
        [FromHeader(Name = "Idempotency-Key")] Guid idempotencyKey,
        CreateBookingRequest request,
        CancellationToken cancellationToken)
    {
        var input = new CreateBookingInput(
            idempotencyKey,
            request.PerformanceId,
            request.CustomerEmail,
            request.Seats);

        var result = await bookingService.CreateAsync(
            input,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        var booking = result.Value!;

        return CreatedAtAction(
            nameof(GetById),
            new { id = booking.Id },
            booking);
    }

    // Chapter 4 exposes cancellation to authenticated administrators only.
    // Guest cancellation will use scoped access credentials in Chapter 5.
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<BookingDto>> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var result = await bookingService.CancelAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToActionResult(result.Error!);
    }
}
```

The expiration worker also needs to recognize normal shutdown cancellation. The following replacement preserves the current cycle while adding that branch:

### src/OperaHouse.Booking.Infrastructure/Bookings/BookingExpirationWorker.cs

Replace this file with the following.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OperaHouse.Booking.Application.Bookings;
using OperaHouse.Booking.Domain.Bookings;
using OperaHouse.Contracts.Events;

namespace OperaHouse.Booking.Infrastructure.Bookings;

public sealed class BookingExpirationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<BookingOptions> bookingOptions,
    TimeProvider timeProvider,
    ILogger<BookingExpirationWorker> logger) : BackgroundService
{
    private readonly BookingOptions _options = bookingOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Booking expiration worker started.");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ExpireBookingsAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Booking expiration cycle failed.");
            }
            
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(
                        _options
                            .ExpirationCheckIntervalSeconds),
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ExpireBookingsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ExpiringBooking> candidates;

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider
                .GetRequiredService<IBookingRepository>();

            candidates =
                await repository.GetExpiringBookingAsync(
                    timeProvider.GetUtcNow(),
                    _options.ExpirationBatchSize,
                    cancellationToken);
        }

        foreach (var candidate in candidates)
        {
            await using var scope =
                scopeFactory.CreateAsyncScope();

            var repository = scope.ServiceProvider
                .GetRequiredService<IBookingRepository>();
            
            var currentTime = timeProvider.GetUtcNow();
            
            var bookingExpired = new BookingExpired(
                MessageId: Guid.NewGuid(),
                CorrelationId: Guid.NewGuid(),
                BookingId: candidate.BookingId,
                PerformanceId:
                candidate.PerformanceId,
                CustomerEmail: candidate.CustomerEmail,
                Seats: candidate.Seats,
                OccurredAt: currentTime);
            
            var expired =
                await repository.TryExpireAsync(
                    candidate,
                    bookingExpired,
                    currentTime,
                    cancellationToken);

            if (expired)
            {
                logger.LogInformation(
                    "Expired booking {BookingId} and released {Seats} seats.",
                    candidate.BookingId,
                    candidate.Seats);
            }
        }
    }
}
```

The publisher already enables confirms and mandatory routing. Its existing structured-log call has an extra argument without a placeholder; the replacement below includes the correlation placeholder and otherwise preserves its behavior.

### src/OperaHouse.Messaging/RabbitMqPublisher.cs

Replace this file with the following.

```csharp
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
            "Publishing outbox message {MessageId} with correlation {CorrelationId} of type {MessageType} to exchange {ExchangeName} with routing key {RoutingKey}",
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
```

## 7. Single-message DLQ replay tool

This tool replays only the **next** message in the chosen notification DLQ and only when its message ID matches the ID you provide. Inspect the DLQ in the RabbitMQ Management UI first using a requeueing inspection mode. Fix the underlying failure before replaying.

The tool preserves identity/correlation headers and confirms mandatory publication before ACKing the DLQ copy. `--reset-retries` deliberately resets its retry budget. If publication succeeds but the final ACK fails, duplicate delivery remains possible with the same message ID.

### tools/OperaHouse.DlqReplay/OperaHouse.DlqReplay.csproj

Create this file.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <ProjectReference Include="../../src/OperaHouse.Messaging/OperaHouse.Messaging.csproj" />
  </ItemGroup>
</Project>
```

### tools/OperaHouse.DlqReplay/Program.cs

Create this file.

```csharp
using Microsoft.Extensions.Configuration;
using OperaHouse.Messaging;
using RabbitMQ.Client;

if (args.Length is < 2 or > 3
    || !Guid.TryParse(args[1], out var expectedMessageId)
    || (args.Length == 3 && args[2] != "--reset-retries"))
{
    Console.Error.WriteLine("Usage: <booking.created|booking.expired|booking.cancelled> <message-id> [--reset-retries]");
    return 2;
}

var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
var options = new RabbitMqOptions();
configuration.GetSection("RabbitMq").Bind(options);
var target = args[0] switch
{
    "booking.created" => (options.BookingCreatedDeadLetterQueueName, options.BookingCreatedRoutingKey),
    "booking.expired" => (options.BookingExpiredDeadLetterQueueName, options.BookingExpiredRoutingKey),
    "booking.cancelled" => (options.BookingCancelledDeadLetterQueueName, options.BookingCancelledRoutingKey),
    _ => throw new ArgumentException("Unknown notification event type.")
};

var factory = new ConnectionFactory
{
    HostName = options.HostName, Port = options.Port,
    UserName = options.UserName, Password = options.Password,
    AutomaticRecoveryEnabled = false, ClientProvidedName = "operahouse-dlq-replay"
};

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
await using var connection = await factory.CreateConnectionAsync(timeout.Token);
await using var readChannel = await connection.CreateChannelAsync(cancellationToken: timeout.Token);
await using var publishChannel = await connection.CreateChannelAsync(
    new CreateChannelOptions(publisherConfirmationsEnabled: true,
        publisherConfirmationTrackingEnabled: true), timeout.Token);

var delivery = await readChannel.BasicGetAsync(target.Item1, autoAck: false,
    cancellationToken: timeout.Token);
if (delivery is null)
{
    Console.WriteLine("Dead-letter queue is empty.");
    return 0;
}

if (!Guid.TryParse(delivery.BasicProperties.MessageId, out var actualId)
    || actualId != expectedMessageId)
{
    await readChannel.BasicNackAsync(delivery.DeliveryTag, false, true, timeout.Token);
    Console.Error.WriteLine("The queue's next message does not match the requested ID; it was requeued.");
    return 3;
}

var headers = delivery.BasicProperties.Headers?.ToDictionary(x => x.Key, x => x.Value)
    ?? new Dictionary<string, object?>();
headers["x-replayed-at"] = DateTimeOffset.UtcNow.ToString("O");
if (args.Length == 3)
    headers["x-retry-count"] = 0;

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

// If publication throws, disposing the read channel requeues the unacknowledged source.
// If publication succeeds but ACK fails, duplicates are possible and retain the same ID.
await publishChannel.BasicPublishAsync(options.ExchangeName, target.Item2,
    mandatory: true, basicProperties: properties, body: delivery.Body,
    cancellationToken: timeout.Token);
await readChannel.BasicAckAsync(delivery.DeliveryTag, false, timeout.Token);
Console.WriteLine($"Replayed {expectedMessageId} to {target.Item2}.");
return 0;
```

Configure the tool through environment variables, using your local broker credentials. It does not read the Notification Worker's User Secrets:

```zsh
export RabbitMq__HostName=127.0.0.1
export RabbitMq__Port=5672
export RabbitMq__UserName=operahouse
read -s "RabbitMq__Password?Local RabbitMQ password: "
export RabbitMq__Password
```

Run it with the message ID you inspected:

```zsh
dotnet run --project tools/OperaHouse.DlqReplay -- booking.expired MESSAGE_ID --reset-retries
```

Replace `MESSAGE_ID` with that existing message's GUID. A nonmatching next message is requeued, and the tool exits without replaying it. Keep this as a deliberate operator command, not an automatically exposed HTTP endpoint.

## 8. Automated checks

Create a conventional xUnit test project and reference the existing projects:

```zsh
dotnet new xunit --framework net10.0 --name OperaHouse.Tests --output tests/OperaHouse.Tests
dotnet add tests/OperaHouse.Tests/OperaHouse.Tests.csproj reference src/OperaHouse.Booking.Infrastructure/OperaHouse.Booking.Infrastructure.csproj src/OperaHouse.Notification.Infrastructure/OperaHouse.Notification.Infrastructure.csproj src/OperaHouse.Notification.Worker/OperaHouse.Notification.Worker.csproj
dotnet add tests/OperaHouse.Tests/OperaHouse.Tests.csproj package Microsoft.EntityFrameworkCore --version 10.0.9
dotnet add tests/OperaHouse.Tests/OperaHouse.Tests.csproj package Microsoft.EntityFrameworkCore.Relational --version 10.0.9
dotnet sln OperaHouse.sln add tests/OperaHouse.Tests/OperaHouse.Tests.csproj tools/OperaHouse.DlqReplay/OperaHouse.DlqReplay.csproj
```

Add this item group to the generated test project. The explicit EF Core versions above match the worker's current 10.0.9 design dependency and prevent the test host selecting an older transitive EF Core assembly.

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

The template example test can be removed. Add these four files.

### tests/OperaHouse.Tests/NotificationServiceTests.cs

Create this file.

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OperaHouse.Contracts.Events;
using OperaHouse.Notification.Application.Inbox;
using OperaHouse.Notification.Application.Notifications;
using OperaHouse.Notification.Domain.Inbox;
using OperaHouse.Notification.Domain.Notifications;
using OperaHouse.Notification.Infrastructure.Email;
using Xunit;

namespace OperaHouse.Tests;

public sealed class NotificationServiceTests
{
    private static BookingExpired Expired(Guid? messageId = null) => new(
        messageId ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "customer@example.com", 2, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Expiration_sends_to_event_recipient_and_records_completion()
    {
        var store = new MemoryStore();
        var sender = new RecordingSender();
        var service = CreateService(store, sender);
        var message = Expired();

        Assert.Equal(NotificationProcessingResult.Processed,
            await service.ProcessBookingExpiredAsync(message, default));

        var notification = Assert.Single(store.Notifications);
        Assert.Equal(message.CustomerEmail, sender.Recipient);
        Assert.Equal(NotificationStatus.Sent, notification.Status);
        Assert.Equal(InboxMessageStatus.Processed, Assert.Single(store.Inbox).Status);
        Assert.Contains("2 reserved seat(s)", notification.Body);
    }

    [Fact]
    public async Task Completed_duplicate_does_not_send_a_second_email()
    {
        var store = new MemoryStore();
        var sender = new RecordingSender();
        var service = CreateService(store, sender);
        var message = Expired();

        await service.ProcessBookingExpiredAsync(message, default);
        Assert.Equal(NotificationProcessingResult.Duplicate,
            await service.ProcessBookingExpiredAsync(message, default));
        Assert.Equal(1, sender.Attempts);
        Assert.Single(store.Inbox);
        Assert.Single(store.Notifications);
    }

    [Fact]
    public async Task Failed_send_is_recorded_and_retry_reuses_existing_records()
    {
        var store = new MemoryStore();
        var sender = new RecordingSender { FailNext = true };
        var service = CreateService(store, sender);
        var message = Expired();

        await Assert.ThrowsAsync<EmailDeliveryException>(
            () => service.ProcessBookingExpiredAsync(message, default));
        Assert.Equal(NotificationStatus.Failed, Assert.Single(store.Notifications).Status);
        Assert.Equal(InboxMessageStatus.Failed, Assert.Single(store.Inbox).Status);

        await service.ProcessBookingExpiredAsync(message, default);
        Assert.Equal(2, sender.Attempts);
        Assert.Single(store.Notifications);
        Assert.Single(store.Inbox);
        Assert.Equal(NotificationStatus.Sent, store.Notifications[0].Status);
    }

    [Fact]
    public async Task Cancellation_uses_its_own_consumer_identity_and_notification_type()
    {
        var store = new MemoryStore();
        var sender = new RecordingSender();
        var service = CreateService(store, sender);
        var message = Expired();

        await service.ProcessBookingCancelledAsync(new BookingCancelled(
            message.MessageId, message.CorrelationId, message.BookingId,
            message.PerformanceId, message.CustomerEmail, message.Seats, message.OccurredAt), default);

        Assert.Equal("OperaHouse.Notification.BookingCancelled", Assert.Single(store.Inbox).Consumer);
        Assert.Equal("BookingCancellation", Assert.Single(store.Notifications).Type);
    }

    [Fact]
    public async Task Shutdown_is_not_recorded_as_a_permanent_processing_failure()
    {
        var store = new MemoryStore();
        using var cancellation = new CancellationTokenSource();
        var sender = new RecordingSender { Shutdown = cancellation };
        var service = CreateService(store, sender);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.ProcessBookingExpiredAsync(Expired(), cancellation.Token));
        Assert.Equal(NotificationStatus.Pending, Assert.Single(store.Notifications).Status);
        Assert.Equal(InboxMessageStatus.Processing, Assert.Single(store.Inbox).Status);
    }

    [Fact]
    public async Task Database_failure_before_submission_prevents_email_send()
    {
        var store = new MemoryStore { FailOnSave = 1 };
        var sender = new RecordingSender();
        var service = CreateService(store, sender);

        await Assert.ThrowsAsync<IOException>(() => service.ProcessBookingExpiredAsync(Expired(), default));
        Assert.Equal(0, sender.Attempts);
    }

    [Fact]
    public async Task Database_failure_after_submission_exposes_an_uncertain_send_outcome()
    {
        var store = new MemoryStore { FailOnSave = 2 };
        var sender = new RecordingSender();
        var service = CreateService(store, sender);

        await Assert.ThrowsAsync<IOException>(() => service.ProcessBookingExpiredAsync(Expired(), default));
        Assert.Equal(1, sender.Attempts);
        Assert.Equal(NotificationStatus.Pending, store.LastPersistedNotificationStatus);
        Assert.Equal(InboxMessageStatus.Processing, store.LastPersistedInboxStatus);
    }

    [Fact]
    public async Task Invalid_smtp_recipient_is_permanent_without_a_network_call()
    {
        var sender = new SmtpEmailSender(Options.Create(new EmailOptions
        {
            Host = "localhost", FromAddress = "sender@example.com"
        }), NullLogger<SmtpEmailSender>.Instance);

        var error = await Assert.ThrowsAsync<EmailDeliveryException>(() =>
            sender.SendAsync("not-an-email-address", "Subject", "Body", default));
        Assert.False(error.IsTransient);
    }

    private static NotificationService CreateService(MemoryStore store, RecordingSender sender) =>
        new(store, store, sender, store, NullLogger<NotificationService>.Instance);

    private sealed class RecordingSender : IEmailSender
    {
        public int Attempts { get; private set; }
        public string? Recipient { get; private set; }
        public bool FailNext { get; set; }
        public CancellationTokenSource? Shutdown { get; init; }

        public Task SendAsync(string recipient, string subject, string body, CancellationToken token)
        {
            Attempts++;
            Recipient = recipient;
            if (Shutdown is not null)
            {
                Shutdown.Cancel();
                throw new OperationCanceledException(token);
            }
            if (FailNext)
            {
                FailNext = false;
                throw new EmailDeliveryException("Temporary test failure.", true);
            }
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryStore : IInboxRepository, INotificationRepository, INotificationUnitOfWork
    {
        public List<InboxMessage> Inbox { get; } = [];
        public List<NotificationMessage> Notifications { get; } = [];
        public int FailOnSave { get; init; }
        public int Saves { get; private set; }
        public NotificationStatus LastPersistedNotificationStatus { get; private set; }
        public InboxMessageStatus LastPersistedInboxStatus { get; private set; }

        public Task<InboxMessage?> GetByMessageAsync(Guid id, string consumer, CancellationToken token) =>
            Task.FromResult(Inbox.SingleOrDefault(x => x.MessageId == id && x.Consumer == consumer));
        public Task AddAsync(InboxMessage message, CancellationToken token)
        {
            Inbox.Add(message);
            return Task.CompletedTask;
        }
        public Task<NotificationMessage?> GetByInboxMessageIdAsync(Guid id, CancellationToken token) =>
            Task.FromResult(Notifications.SingleOrDefault(x => x.InboxMessageId == id));
        public Task AddAsync(NotificationMessage message, CancellationToken token)
        {
            Notifications.Add(message);
            return Task.CompletedTask;
        }
        public Task SaveChangesAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Saves++;
            if (Saves == FailOnSave)
                throw new IOException("Simulated database save failure.");
            LastPersistedNotificationStatus = Notifications[0].Status;
            LastPersistedInboxStatus = Inbox[0].Status;
            return Task.CompletedTask;
        }
    }
}
```

### tests/OperaHouse.Tests/BookingDomainTests.cs

Create this file.

```csharp
using OperaHouse.Booking.Domain.Bookings;
using Xunit;
using Reservation = OperaHouse.Booking.Domain.Bookings.Booking;

namespace OperaHouse.Tests;

public sealed class BookingDomainTests
{
    [Fact]
    public void Reservation_rejects_zero_seats()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentOutOfRangeException>(() => new Reservation(Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "customer@example.com", 0, now, now.AddMinutes(15)));
    }

    [Fact]
    public void Reservation_rejects_a_deadline_before_creation()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() => new Reservation(Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "customer@example.com", 1, now, now));
    }

    [Fact]
    public void Idempotency_comparison_checks_seats_performance_and_normalized_email()
    {
        var now = DateTimeOffset.UtcNow;
        var performanceId = Guid.NewGuid();
        var reservation = new Reservation(Guid.NewGuid(), Guid.NewGuid(), performanceId,
            "Customer@Example.com", 2, now, now.AddMinutes(15));
        Assert.Equal(BookingStatus.Pending, reservation.Status);
        Assert.True(reservation.HasSameReservationDetails(performanceId, "customer@example.com", 2));
        Assert.False(reservation.HasSameReservationDetails(performanceId, "customer@example.com", 3));
        Assert.False(reservation.HasSameReservationDetails(Guid.NewGuid(), "customer@example.com", 2));
    }
}
```

### tests/OperaHouse.Tests/BookingDatabaseTests.cs

Create this file.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using OperaHouse.Booking.Application.Bookings;
using OperaHouse.Booking.Domain.Bookings;
using OperaHouse.Booking.Domain.Performances;
using OperaHouse.Booking.Infrastructure.Bookings;
using OperaHouse.Booking.Infrastructure.Persistence;
using OperaHouse.Contracts.Events;
using OperaHouse.Messaging;
using Xunit;
using Reservation = OperaHouse.Booking.Domain.Bookings.Booking;

namespace OperaHouse.Tests;

public sealed class BookingDatabaseTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task Concurrent_requests_cannot_oversell_the_last_two_seats()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var performance = await database.CreatePerformanceAsync(2, now);
        var results = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => database.ReserveAsync(performance.Id, Guid.NewGuid(), now)));
        Assert.Equal(2, results.Count(x => x.Outcome == BookingCreationOutcome.Created));
        await using var context = database.Context();
        Assert.Equal(0, (await context.Performances.SingleAsync()).AvailableSeats);
        Assert.Equal(2, await context.Bookings.CountAsync());
        Assert.Equal(2, await context.OutboxMessages.CountAsync());
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Concurrent_same_key_requests_return_the_same_last_seat_reservation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var performance = await database.CreatePerformanceAsync(1, now);
        var key = Guid.NewGuid();
        var results = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => database.ReserveAsync(performance.Id, key, now)));
        Assert.Equal(1, results.Count(x => x.Outcome == BookingCreationOutcome.Created));
        Assert.Equal(4, results.Count(x => x.Outcome == BookingCreationOutcome.Existing));
        Assert.Equal(1, results.Select(x => x.Booking!.Id).Distinct().Count());
        await using var context = database.Context();
        Assert.Equal(1, await context.OutboxMessages.CountAsync());
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Same_key_with_different_details_is_a_conflict()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var performance = await database.CreatePerformanceAsync(5, now);
        var key = Guid.NewGuid();
        await database.ReserveAsync(performance.Id, key, now);
        var conflict = await database.ReserveAsync(performance.Id, key, now, seats: 2);
        Assert.Equal(BookingCreationOutcome.IdempotencyConflict, conflict.Outcome);
        await using var context = database.Context();
        Assert.Equal(4, (await context.Performances.SingleAsync()).AvailableSeats);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Repeated_expiration_releases_seats_and_creates_an_event_once()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var performance = await database.CreatePerformanceAsync(2, now);
        var reservation = (await database.ReserveAsync(performance.Id, Guid.NewGuid(), now)).Booking!;
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => database.ExpireAsync(reservation, now.AddMinutes(16))));
        Assert.Equal(1, outcomes.Count(x => x));
        await using var context = database.Context();
        Assert.Equal(2, (await context.Performances.SingleAsync()).AvailableSeats);
        Assert.Equal(BookingStatus.Expired, (await context.Bookings.SingleAsync()).Status);
        Assert.Equal(1, await context.OutboxMessages.CountAsync(x => x.Type == nameof(BookingExpired)));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Repeated_cancellation_does_not_release_or_publish_twice()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var performance = await database.CreatePerformanceAsync(1, now);
        var reservation = (await database.ReserveAsync(performance.Id, Guid.NewGuid(), now)).Booking!;
        Assert.Equal(BookingCancellationOutcome.Cancelled,
            await database.CancelAsync(reservation.Id, now.AddMinutes(1)));
        Assert.Equal(BookingCancellationOutcome.AlreadyCancelled,
            await database.CancelAsync(reservation.Id, now.AddMinutes(2)));
        await using var context = database.Context();
        Assert.Equal(1, (await context.Performances.SingleAsync()).AvailableSeats);
        Assert.Equal(1, await context.OutboxMessages.CountAsync(x => x.Type == nameof(BookingCancelled)));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Cancellation_racing_expiration_has_only_one_winning_transition()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var performance = await database.CreatePerformanceAsync(1, now);
        var reservation = (await database.ReserveAsync(performance.Id, Guid.NewGuid(), now)).Booking!;
        // Simulate a cancel request that began before the deadline and an expiry
        // check that began after it; their database operations can overlap.
        await Task.WhenAll(
            database.CancelAsync(reservation.Id, now.AddMinutes(14)),
            database.ExpireAsync(reservation, now.AddMinutes(16)));
        await using var context = database.Context();
        Assert.Equal(1, (await context.Performances.SingleAsync()).AvailableSeats);
        Assert.True((await context.Bookings.SingleAsync()).Status is BookingStatus.Cancelled or BookingStatus.Expired);
        Assert.Equal(1, await context.OutboxMessages.CountAsync(x =>
            x.Type == nameof(BookingCancelled) || x.Type == nameof(BookingExpired)));
    }
}

internal sealed class TestDatabase(string adminConnection, string name, string connectionString) : IAsyncDisposable
{
    public string ConnectionString { get; } = connectionString;
    public BookingDbContext Context() => new(new DbContextOptionsBuilder<BookingDbContext>()
        .UseNpgsql(ConnectionString).Options);

    public static async Task<TestDatabase> CreateAsync()
    {
        var admin = Environment.GetEnvironmentVariable("OPERAHOUSE_TEST_POSTGRES")
            ?? "Host=127.0.0.1;Port=54400;Database=postgres;Username=operahouse;Password=operahouse";
        var name = "oh_ch4_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
        await command.ExecuteNonQueryAsync();
        var builder = new NpgsqlConnectionStringBuilder(admin) { Database = name };
        var database = new TestDatabase(admin, name, builder.ConnectionString);
        try
        {
            await using var context = database.Context();
            await context.Database.MigrateAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public async Task<Performance> CreatePerformanceAsync(int capacity, DateTimeOffset now)
    {
        await using var context = Context();
        var performance = new Performance("Test performance", "Test venue", now.AddDays(1), capacity, now);
        performance.Publish(now);
        context.Performances.Add(performance);
        await context.SaveChangesAsync();
        return performance;
    }

    public async Task<BookingCreationPersistenceResult> ReserveAsync(Guid performanceId, Guid key,
        DateTimeOffset now, int seats = 1)
    {
        await using var context = Context();
        var reservation = new Reservation(Guid.NewGuid(), key, performanceId,
            "customer@example.com", seats, now, now.AddMinutes(15));
        var message = new BookingCreated(Guid.NewGuid(), Guid.NewGuid(), reservation.Id,
            performanceId, reservation.CustomerEmail, seats, reservation.ExpiresAt, now);
        return await new BookingRepository(context, Options.Create(new RabbitMqOptions()))
            .TryCreateAsync(reservation, message, now, default);
    }

    public async Task<bool> ExpireAsync(Reservation reservation, DateTimeOffset now)
    {
        await using var context = Context();
        var candidate = new ExpiringBooking(reservation.Id, reservation.PerformanceId,
            reservation.CustomerEmail, reservation.Seats);
        var message = new BookingExpired(Guid.NewGuid(), Guid.NewGuid(), reservation.Id,
            reservation.PerformanceId, reservation.CustomerEmail, reservation.Seats, now);
        return await new BookingRepository(context, Options.Create(new RabbitMqOptions()))
            .TryExpireAsync(candidate, message, now, default);
    }

    public async Task<BookingCancellationOutcome> CancelAsync(Guid id, DateTimeOffset now)
    {
        await using var context = Context();
        return await new BookingRepository(context, Options.Create(new RabbitMqOptions()))
            .TryCancelAsync(id, Guid.NewGuid(), now, default);
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
```

### tests/OperaHouse.Tests/RabbitMqTests.cs

Create this file.

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OperaHouse.Booking.Infrastructure.Outbox;
using OperaHouse.Booking.Infrastructure.Persistence;
using OperaHouse.Booking.Infrastructure.Persistence.Outbox;
using OperaHouse.Contracts.Events;
using OperaHouse.Messaging;
using OperaHouse.Notification.Application.Notifications;
using RabbitMQ.Client;
using Xunit;
using NotificationWorker = OperaHouse.Notification.Worker.Worker;

namespace OperaHouse.Tests;

public sealed class RabbitMqTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task Expiration_uses_its_own_consumer_and_delayed_retry_route()
    {
        var spy = new NotificationServiceSpy { FailFirst = true };
        await WithWorkerAsync(spy, async (options, connection) =>
        {
            var message = Expiration();
            await PublishAsync(options, message);
            await spy.Completed.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(2, spy.ExpiredAttempts);
            Assert.Equal(0, spy.CreatedAttempts);
        });
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Permanent_expiration_failure_goes_to_its_dlq_without_retry()
    {
        var spy = new NotificationServiceSpy { Permanent = true };
        await WithWorkerAsync(spy, async (options, connection) =>
        {
            await PublishAsync(options, Expiration());
            await using var channel = await connection.CreateChannelAsync();
            await WaitAsync(async () =>
                (await channel.QueueDeclarePassiveAsync(options.BookingExpiredDeadLetterQueueName)).MessageCount == 1);
            Assert.Equal(1, spy.ExpiredAttempts);
        });
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Unroutable_outbox_message_stays_pending_and_records_a_failure()
    {
        await using var database = await TestDatabase.CreateAsync();
        var options = BrokerOptions();
        var message = Expiration();
        await using (var context = database.Context())
        {
            context.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(), MessageId = message.MessageId,
                CorrelationId = message.CorrelationId, Type = nameof(BookingExpired),
                RoutingKey = options.BookingExpiredRoutingKey,
                Payload = JsonSerializer.Serialize(message), OccurredAt = message.OccurredAt
            });
            await context.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptions<RabbitMqOptions>>(Options.Create(options));
        services.AddDbContext<BookingDbContext>(builder => builder.UseNpgsql(database.ConnectionString));
        services.AddScoped<RabbitMqPublisher>();
        await using var provider = services.BuildServiceProvider();
        using var worker = new OutboxPublisherWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OutboxPublisherWorker>.Instance);
        try
        {
            await worker.StartAsync(default);
            await WaitAsync(async () =>
            {
                await using var context = database.Context();
                return await context.OutboxMessages.AnyAsync(x => x.PublishAttempts > 0);
            });
            await using var context = database.Context();
            var pending = await context.OutboxMessages.SingleAsync();
            Assert.True(pending.ProcessedAt is null);
            Assert.True(pending.PublishAttempts > 0);
        }
        finally
        {
            await worker.StopAsync(default);
            await using var connection = await Factory(options).CreateConnectionAsync();
            await using var channel = await connection.CreateChannelAsync();
            await channel.ExchangeDeleteAsync(options.ExchangeName);
        }
    }

    private static BookingExpired Expiration() => new(Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), "customer@example.com", 1, DateTimeOffset.UtcNow);

    private static async Task PublishAsync(RabbitMqOptions options, BookingExpired message) =>
        await new RabbitMqPublisher(Options.Create(options), NullLogger<RabbitMqPublisher>.Instance)
            .PublishJsonAsync(JsonSerializer.Serialize(message), options.BookingExpiredRoutingKey,
                message.MessageId, message.CorrelationId, nameof(BookingExpired), default);

    private static async Task WithWorkerAsync(NotificationServiceSpy spy,
        Func<RabbitMqOptions, IConnection, Task> check)
    {
        var options = BrokerOptions();
        await using var connection = await Factory(options).CreateConnectionAsync();
        var services = new ServiceCollection();
        services.AddSingleton<INotificationService>(spy);
        await using var provider = services.BuildServiceProvider();
        using var worker = new NotificationWorker(NullLogger<NotificationWorker>.Instance,
            Options.Create(options), provider.GetRequiredService<IServiceScopeFactory>());
        try
        {
            await worker.StartAsync(default);
            await WaitAsync(async () =>
            {
                await using var probe = await connection.CreateChannelAsync();
                try
                {
                    await probe.QueueDeclarePassiveAsync(options.BookingCancelledQueueName);
                    return true;
                }
                catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
                {
                    return false;
                }
            });
            await check(options, connection);
        }
        finally
        {
            await worker.StopAsync(default);
            await using var channel = await connection.CreateChannelAsync();
            foreach (var queue in new[]
            {
                options.BookingCreatedQueueName, options.BookingCreatedRetryQueueName,
                options.BookingCreatedDeadLetterQueueName, options.BookingExpiredQueueName,
                options.BookingExpiredRetryQueueName, options.BookingExpiredDeadLetterQueueName,
                options.BookingCancelledQueueName, options.BookingCancelledRetryQueueName,
                options.BookingCancelledDeadLetterQueueName
            })
                await channel.QueueDeleteAsync(queue);
            await channel.ExchangeDeleteAsync(options.ExchangeName);
            await channel.ExchangeDeleteAsync(options.RetryExchangeName);
            await channel.ExchangeDeleteAsync(options.DeadLetterExchangeName);
        }
    }

    private static async Task WaitAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!await condition())
            await Task.Delay(100, timeout.Token);
    }

    private static ConnectionFactory Factory(RabbitMqOptions options) => new()
    {
        HostName = options.HostName, Port = options.Port,
        UserName = options.UserName, Password = options.Password,
        AutomaticRecoveryEnabled = false
    };

    private static RabbitMqOptions BrokerOptions()
    {
        var prefix = "oh_ch4_" + Guid.NewGuid().ToString("N");
        return new RabbitMqOptions
        {
            HostName = Environment.GetEnvironmentVariable("RabbitMq__HostName") ?? "127.0.0.1",
            Port = int.Parse(Environment.GetEnvironmentVariable("RabbitMq__Port") ?? "5672"),
            UserName = Environment.GetEnvironmentVariable("RabbitMq__UserName") ?? "operahouse",
            Password = Environment.GetEnvironmentVariable("RabbitMq__Password") ?? "operahouse",
            ExchangeName = prefix + ".events", RetryExchangeName = prefix + ".retry",
            DeadLetterExchangeName = prefix + ".dead",
            BookingCreatedQueueName = prefix + ".created",
            BookingCreatedRetryQueueName = prefix + ".created.retry",
            BookingCreatedDeadLetterQueueName = prefix + ".created.dead",
            BookingExpiredQueueName = prefix + ".expired",
            BookingExpiredRetryQueueName = prefix + ".expired.retry",
            BookingExpiredDeadLetterQueueName = prefix + ".expired.dead",
            BookingCancelledQueueName = prefix + ".cancelled",
            BookingCancelledRetryQueueName = prefix + ".cancelled.retry",
            BookingCancelledDeadLetterQueueName = prefix + ".cancelled.dead",
            BookingCreatedRetryDelayMilliseconds = 200,
            BookingExpiredRetryDelayMilliseconds = 200,
            BookingCancelledRetryDelayMilliseconds = 200,
            BookingCreatedMaxRetryAttempts = 1,
            BookingExpiredMaxRetryAttempts = 1,
            BookingCancelledMaxRetryAttempts = 1
        };
    }

    private sealed class NotificationServiceSpy : INotificationService
    {
        private int _createdAttempts;
        private int _expiredAttempts;
        public int CreatedAttempts => Volatile.Read(ref _createdAttempts);
        public int ExpiredAttempts => Volatile.Read(ref _expiredAttempts);
        public bool FailFirst { get; init; }
        public bool Permanent { get; init; }
        public TaskCompletionSource<bool> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<NotificationProcessingResult> ProcessBookingCreatedAsync(BookingCreated message, CancellationToken token)
        {
            Interlocked.Increment(ref _createdAttempts);
            return Task.FromResult(NotificationProcessingResult.Processed);
        }
        public Task<NotificationProcessingResult> ProcessBookingExpiredAsync(BookingExpired message, CancellationToken token)
        {
            var attempt = Interlocked.Increment(ref _expiredAttempts);
            if (Permanent) throw new EmailDeliveryException("Permanent test failure.", false);
            if (FailFirst && attempt == 1) throw new EmailDeliveryException("Temporary test failure.", true);
            Completed.TrySetResult(true);
            return Task.FromResult(NotificationProcessingResult.Processed);
        }
        public Task<NotificationProcessingResult> ProcessBookingCancelledAsync(BookingCancelled message, CancellationToken token) =>
            Task.FromResult(NotificationProcessingResult.Processed);
    }
}
```

### Running the checks

Unit tests use in-memory fakes for application dependencies and do not send real email:

```zsh
dotnet build OperaHouse.sln
dotnet test tests/OperaHouse.Tests/OperaHouse.Tests.csproj --filter 'Category!=Integration'
```

Run integration tests against the local Docker services:

```zsh
docker compose up -d
dotnet test tests/OperaHouse.Tests/OperaHouse.Tests.csproj --filter 'Category=Integration'
```

Database tests create and drop uniquely named `oh_ch4_...` test databases. The configured test database user needs permission to create them. Set `OPERAHOUSE_TEST_POSTGRES` to a dedicated local/test PostgreSQL administration connection if the Docker default is not appropriate. Broker tests create and remove uniquely named exchanges/queues. They do not send customer emails.

## 9. Manual acceptance checks

1. Apply Booking migrations with `dotnet ef database update --project src/OperaHouse.Booking.Infrastructure --startup-project src/OperaHouse.Booking.Api` and Notification migrations with `dotnet ef database update --project src/OperaHouse.Notification.Infrastructure --startup-project src/OperaHouse.Notification.Worker`. Use the .NET 10 EF tool matching the current 10.0.9 design package. The database tests cover empty-database migration; also rehearse upgrading a separate test copy of the previous schema.
2. Start the Notification Worker first so all queue bindings exist, then start the API. Verify their PostgreSQL/RabbitMQ health.
3. With an Admin development JWT, create and publish a future performance. Confirm public browsing excludes drafts and past/unavailable performances.
4. Create a booking through `POST /bookings` using a fresh `Idempotency-Key` and a controlled test recipient. Inspect the booking, seat count, outbox, inbox, and received email.
5. Use a short local reservation duration to check expiration. Verify `Expired`, released seats, one logical expiration outbox event, a completed inbox entry, and the expiration email.
6. Create another reservation and call `POST /bookings/{id}/cancel` with an Admin bearer token. Repeat it. Verify the same cancelled outcome, a single release/event, and one completed cancellation notification. Confirm an unauthenticated cancellation is rejected.
7. Test an invalid payload, a temporary service failure, exhausted retries, and a missing route. Inspect the appropriate DLQ, fix the failure, and use the replay tool while preserving the original message ID.
8. Restart RabbitMQ/worker during processing and check recovery and duplicate recognition. Existing SMTP deduplication does not eliminate uncertain sends; the post-submission save-failure unit test documents that limit.
9. Keep the Chapter 4 roadmap verification boxes open until the running database/broker tests and real-email checks pass. Preparing or compiling this guide does not complete the chapter.

## Validation of this draft

- Application replacements compile against the installed .NET 10 SDK and existing cached packages.
- The DLQ replay tool compiles.
- All test sources compile in a scratch validation project. Eleven unit-test bodies are also executed with a scratch-only assertion harness; this is not a claim that the xUnit runner was restored or executed.
- PostgreSQL/RabbitMQ integration tests and real SMTP acceptance are not run because Docker is not running in this session. Those remain required checks after applying the guide.

## References

- [RabbitMQ .NET client](https://www.rabbitmq.com/client-libraries/dotnet-api-guide).
- [Consumer acknowledgements and publisher confirms](https://www.rabbitmq.com/docs/confirms).
- [MailKit SMTP command exceptions](https://mimekit.net/docs/html/T_MailKit_Net_Smtp_SmtpCommandException.htm).
