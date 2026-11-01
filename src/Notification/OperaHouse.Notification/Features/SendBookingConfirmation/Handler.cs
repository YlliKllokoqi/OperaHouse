using OperaHouse.Contracts.Events;
using OperaHouse.Notification.Domain.Notifications;
using OperaHouse.Notification.Processing;

namespace OperaHouse.Notification.Features;

public sealed class SendBookingConfirmation(NotificationProcessor processor)
{
    public Task<NotificationProcessingResult> HandleAsync(
        BookingCreated message,
        CancellationToken cancellationToken)
    {
        BookingEventValidator.Validate(message);

        return processor.ProcessAsync(message.MessageId,
            "OperaHouse.Notification.Worker", nameof(BookingCreated),
            new NotificationMessage
            {
                Type = "BookingConfirmation",
                Recipient = message.CustomerEmail,
                Subject = "Booking received",
                Body = $"Your booking {message.BookingId} was received."
            }, cancellationToken);
    }
}
