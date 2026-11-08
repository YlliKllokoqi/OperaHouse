using System.ComponentModel.DataAnnotations;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using OperaHouse.Contracts.Events;
using OperaHouse.Notification.Email;
using OperaHouse.Notification.Features;
using OperaHouse.Notification.Processing;
using OperaHouse.Notification.Persistence;
using OperaHouse.Notification.Domain.Inbox;
using OperaHouse.Notification.Domain.Notifications;

namespace OperaHouse.RegressionTests;

internal static class NotificationTests
{
    public static BookingExpired Expired() => new(Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), "guest@example.com", 1, DateTimeOffset.UtcNow);

    public static BookingCreated Created()
    {
        var message = Expired();
        return new(message.MessageId, message.CorrelationId, message.BookingId,
            message.PerformanceId, message.CustomerEmail, message.Seats,
            message.OccurredAt.AddMinutes(15), message.OccurredAt);
    }

    public static async Task RunAsync()
    {
        CheckSmtpFailureClassification();

        foreach (var created in new[] { true, false })
        {
            var eventName = created ? nameof(BookingCreated) : nameof(BookingExpired);
            var store = new MemoryStore { SaveException = new IOException("Database unavailable") };
            var sender = new EmailSender();
            var service = store.Processor(sender);
            var createdEvent = Created();
            var expiredEvent = Expired();
            Task Process() => created
                ? new SendBookingConfirmation(service).HandleAsync(createdEvent, default)
                : new SendBookingExpiration(service).HandleAsync(expiredEvent, default);
            await Check.ThrowsAsync<IOException>(Process);
            Check.That(store.Saves == 1 && sender.Sends == 1
                && store.Notifications.Single().Status == NotificationStatus.Sent
                && store.Notifications.Single().FailureReason is null
                && store.Inbox.Single().Status == InboxMessageStatus.Processed,
                $"{eventName}: save failure does not relabel accepted email or attempt a failure save");

            var saveFailure = new IOException("Database unavailable");
            var sendFailure = new PermanentEmailDeliveryException(new InvalidOperationException("Mailbox rejected"));
            store = new MemoryStore { SaveException = saveFailure };
            sender = new EmailSender { Failure = sendFailure };
            service = store.Processor(sender);
            var combinedFailure = await Check.ThrowsAsync<AggregateException>(Process);
            Check.That(store.Saves == 1 && combinedFailure.InnerExceptions.SequenceEqual([sendFailure, saveFailure]),
                $"{eventName}: failure recording a rejection preserves both causes for retry");

            store = new MemoryStore();
            sender = new EmailSender { Failure = new IOException("SMTP unavailable") };
            service = store.Processor(sender);
            await Check.ThrowsAsync<IOException>(Process);
            Check.That(store.Saves == 1
                && store.Notifications.Single().Status == NotificationStatus.Failed
                && store.Notifications.Single().SentAt is null
                && store.Inbox.Single().Status == InboxMessageStatus.Failed
                && store.Inbox.Single().ProcessedAt is null,
                $"{eventName}: actual email failure records consistent failure state");
            sender.Failure = null;
            await Process();
            await Process();
            Check.That(sender.Sends == 2 && store.Notifications.Single().FailureReason is null
                && store.Notifications.Single().FailedAt is null,
                $"{eventName}: retry succeeds and completed duplicate does not send again");

            store = new MemoryStore();
            sender = new EmailSender();
            service = store.Processor(sender);
            foreach (var invalid in new[] { "identity", "correlation", "booking", "performance", "email", "seats", "time", "expiration" })
            {
                createdEvent = Created(); expiredEvent = Expired();
                switch (invalid)
                {
                    case "identity": createdEvent = createdEvent with { MessageId = Guid.Empty }; expiredEvent = expiredEvent with { MessageId = Guid.Empty }; break;
                    case "correlation": createdEvent = createdEvent with { CorrelationId = Guid.Empty }; expiredEvent = expiredEvent with { CorrelationId = Guid.Empty }; break;
                    case "booking": createdEvent = createdEvent with { BookingId = Guid.Empty }; expiredEvent = expiredEvent with { BookingId = Guid.Empty }; break;
                    case "performance": createdEvent = createdEvent with { PerformanceId = Guid.Empty }; expiredEvent = expiredEvent with { PerformanceId = Guid.Empty }; break;
                    case "email": createdEvent = createdEvent with { CustomerEmail = "invalid" }; expiredEvent = expiredEvent with { CustomerEmail = "invalid" }; break;
                    case "seats": createdEvent = createdEvent with { Seats = 0 }; expiredEvent = expiredEvent with { Seats = 0 }; break;
                    case "time": createdEvent = createdEvent with { OccurredAt = default }; expiredEvent = expiredEvent with { OccurredAt = default }; break;
                    case "expiration": if (!created) continue; createdEvent = createdEvent with { ExpiresAt = createdEvent.OccurredAt }; break;
                }
                await Check.ThrowsAsync<ValidationException>(Process);
            }
            Check.That(sender.Sends == 0 && store.Inbox.Count == 0 && store.Saves == 0,
                $"{eventName}: invalid events never reach email or persistence");

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            createdEvent = Created(); expiredEvent = Expired();
            sender.Failure = new OperationCanceledException(cancellation.Token);
            await Check.ThrowsAsync<OperationCanceledException>(() => created
                ? new SendBookingConfirmation(service).HandleAsync(createdEvent, cancellation.Token)
                : new SendBookingExpiration(service).HandleAsync(expiredEvent, cancellation.Token));
            Check.That(store.Saves == 0 && store.Notifications.Single().Status == NotificationStatus.Pending,
                $"{eventName}: shutdown does not record an email failure");
        }
    }

    private static void CheckSmtpFailureClassification()
    {
        (int Code, bool Authentication, bool Permanent)[] cases =
        [
            (451, false, false),
            (454, true, false),
            (535, true, true),
            (550, false, true)
        ];

        foreach (var (code, authentication, permanent) in cases)
        {
            var reply = new SmtpCommandException(SmtpErrorCode.UnexpectedStatusCode,
                (SmtpStatusCode)code, "Test SMTP reply");
            Exception failure = authentication ? new AuthenticationException("Authentication failed", reply) : reply;
            Check.That(SmtpEmailSender.IsPermanentRejection(failure) == permanent,
                $"SMTP {code}: {(permanent ? "permanent rejection" : "temporary failure")}");
        }

        Check.That(SmtpEmailSender.IsPermanentRejection(new ServiceNotAuthenticatedException()),
            "SMTP authentication required is a permanent rejection");
        Check.That(!SmtpEmailSender.IsPermanentRejection(new IOException("SMTP unavailable")),
            "SMTP connection failure remains retryable");
    }
}

internal sealed class EmailSender : IEmailSender
{
    public int Sends { get; private set; }
    public Exception? Failure { get; set; }
    public Task SendAsync(string recipient, string subject, string body, CancellationToken token)
    {
        Sends++;
        return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
    }
}

internal sealed class MemoryStore : INotificationStore
{
    public List<InboxMessage> Inbox { get; } = [];
    public List<NotificationMessage> Notifications { get; } = [];
    public Exception? SaveException { get; init; }
    public int Saves { get; private set; }
    public NotificationProcessor Processor(IEmailSender sender) => new(this, sender);
    public Task<InboxMessage?> GetInboxMessageAsync(Guid id, string consumer, CancellationToken token) =>
        Task.FromResult(Inbox.SingleOrDefault(x => x.MessageId == id && x.Consumer == consumer));
    public Task AddInboxMessageAsync(InboxMessage message, CancellationToken token) { Inbox.Add(message); return Task.CompletedTask; }
    public Task<NotificationMessage?> GetNotificationAsync(Guid id, CancellationToken token) =>
        Task.FromResult(Notifications.SingleOrDefault(x => x.InboxMessageId == id));
    public Task AddNotificationAsync(NotificationMessage message, CancellationToken token) { Notifications.Add(message); return Task.CompletedTask; }
    public Task SaveChangesAsync(CancellationToken token)
    {
        Saves++;
        return SaveException is null ? Task.CompletedTask : Task.FromException(SaveException);
    }
}
