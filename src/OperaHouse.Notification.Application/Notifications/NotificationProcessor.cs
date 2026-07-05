using Microsoft.Extensions.Logging;
using OperaHouse.Contracts.Events;
using OperaHouse.Notification.Application.Inbox;
using OperaHouse.Notification.Domain.Inbox;
using OperaHouse.Notification.Domain.Notifications;

namespace OperaHouse.Notification.Application.Notifications;

public sealed class NotificationProcessor(
    IInboxRepository inboxRepository,
    INotificationRepository notificationRepository,
    IEmailSender emailSender,
    INotificationUnitOfWork unitOfWork,
    ILogger<NotificationProcessor> logger)
    : INotificationProcessor
{
    private const string ConsumerName = "OperaHouse.Notification.Worker";

    public async Task<NotificationProcessingResult> ProcessBookingCreatedAsync(
        BookingCreated message,
        CancellationToken cancellationToken)
    {
        var inboxMessage = await inboxRepository.GetByMessageAsync(
            message.MessageId,
            ConsumerName,
            cancellationToken);

        if (inboxMessage?.Status == InboxMessageStatus.Processed)
        {
            logger.LogInformation(
                "Message {MessageId} was already processed by {Consumer}.",
                message.MessageId,
                ConsumerName);

            return NotificationProcessingResult.Duplicate;
        }

        if (inboxMessage is null)
        {
            inboxMessage = new InboxMessage
            {
                Id = Guid.NewGuid(),
                MessageId = message.MessageId,
                MessageType = nameof(BookingCreated),
                Consumer = ConsumerName,
                Status = InboxMessageStatus.Processing,
                ReceivedAt = DateTimeOffset.UtcNow
            };

            await inboxRepository.AddAsync(
                inboxMessage,
                cancellationToken);
        }
        else
        {
            inboxMessage.Status = InboxMessageStatus.Processing;
            inboxMessage.LastError = null;
        }

        var notification = await notificationRepository.GetByInboxMessageIdAsync(
            inboxMessage.Id,
            cancellationToken);

        if (notification is null)
        {
            notification = new NotificationMessage
            {
                Id = Guid.NewGuid(),
                InboxMessageId = inboxMessage.Id,
                Type = "BookingConfirmation",
                Recipient = message.CustomerEmail,
                Subject = "Booking received",
                Body = $"Your booking {message.BookingId} was received.",
                Status = NotificationStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow
            };

            await notificationRepository.AddAsync(
                notification,
                cancellationToken);
        }

        try
        {
            await emailSender.SendAsync(
                notification.Recipient,
                notification.Subject,
                notification.Body,
                cancellationToken);

            notification.Status = NotificationStatus.Sent;
            notification.SentAt = DateTimeOffset.UtcNow;
            notification.FailedAt = null;
            notification.FailureReason = null;

            inboxMessage.Status = InboxMessageStatus.Processed;
            inboxMessage.ProcessedAt = DateTimeOffset.UtcNow;
            inboxMessage.LastError = null;

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return NotificationProcessingResult.Processed;
        }
        catch (Exception exception)
        {
            notification.Status = NotificationStatus.Failed;
            notification.FailedAt = DateTimeOffset.UtcNow;
            notification.FailureReason = exception.Message;

            inboxMessage.Status = InboxMessageStatus.Failed;
            inboxMessage.LastError = exception.Message;

            await unitOfWork.SaveChangesAsync(cancellationToken);

            throw;
        }
    }
}
