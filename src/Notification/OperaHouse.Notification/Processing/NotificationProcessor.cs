using OperaHouse.Notification.Domain.Inbox;
using OperaHouse.Notification.Domain.Notifications;
using OperaHouse.Notification.Email;
using OperaHouse.Notification.Persistence;

namespace OperaHouse.Notification.Processing;

// shortcut: SMTP acceptance and saving are not atomic; use provider idempotency when supported.
public sealed class NotificationProcessor(INotificationStore store, IEmailSender emailSender)
{
    public async Task<NotificationProcessingResult> ProcessAsync(
        Guid messageId,
        string consumer,
        string messageType,
        NotificationMessage content,
        CancellationToken cancellationToken)
    {
        var inboxMessage = await store.GetInboxMessageAsync(messageId, consumer, cancellationToken);

        if (inboxMessage?.Status == InboxMessageStatus.Processed)
        {
            return NotificationProcessingResult.Duplicate;
        }

        if (inboxMessage is null)
        {
            inboxMessage = new InboxMessage
            {
                Id = Guid.NewGuid(),
                MessageId = messageId,
                MessageType = messageType,
                Consumer = consumer,
                Status = InboxMessageStatus.Processing,
                ReceivedAt = DateTimeOffset.UtcNow
            };

            await store.AddInboxMessageAsync(inboxMessage, cancellationToken);
        }
        else
        {
            inboxMessage.Status = InboxMessageStatus.Processing;
            inboxMessage.LastError = null;
        }

        var notification = await store.GetNotificationAsync(inboxMessage.Id, cancellationToken);

        if (notification is null)
        {
            notification = content;
            notification.Id = Guid.NewGuid();
            notification.InboxMessageId = inboxMessage.Id;
            notification.Status = NotificationStatus.Pending;
            notification.CreatedAt = DateTimeOffset.UtcNow;

            await store.AddNotificationAsync(notification, cancellationToken);
        }

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
            notification.Status = NotificationStatus.Failed;
            notification.SentAt = null;
            notification.FailedAt = DateTimeOffset.UtcNow;
            notification.FailureReason = exception.Message;

            inboxMessage.Status = InboxMessageStatus.Failed;
            inboxMessage.ProcessedAt = null;
            inboxMessage.LastError = exception.Message;

            try
            {
                await store.SaveChangesAsync(cancellationToken);
            }
            catch (Exception saveException) when (
                saveException is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                throw new AggregateException("Could not record the email failure.", exception, saveException);
            }

            throw;
        }

        notification.Status = NotificationStatus.Sent;
        notification.SentAt = DateTimeOffset.UtcNow;
        notification.FailedAt = null;
        notification.FailureReason = null;

        inboxMessage.Status = InboxMessageStatus.Processed;
        inboxMessage.ProcessedAt = DateTimeOffset.UtcNow;
        inboxMessage.LastError = null;

        await store.SaveChangesAsync(cancellationToken);

        return NotificationProcessingResult.Processed;
    }
}
