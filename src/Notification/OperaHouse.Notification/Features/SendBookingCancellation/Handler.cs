using OperaHouse.Contracts.Events;
using OperaHouse.Notification.Domain.Notifications;
using OperaHouse.Notification.Processing;

namespace OperaHouse.Notification.Features;

public sealed class SendBookingCancellation(NotificationProcessor processor)
{
    public Task<NotificationProcessingResult> HandleAsync(
        BookingCancelled message,
        CancellationToken cancellationToken)
    {
        BookingEventValidator.Validate(message);

        return processor.ProcessAsync(message.MessageId,
            "OperaHouse.Notification.BookingCancelled", nameof(BookingCancelled),
            new NotificationMessage
            {
                Type = "BookingCancellation",
                Recipient = message.CustomerEmail,
                Subject = "Your OperaHouse reservation was cancelled",
                Body = $"Your reservation for booking {message.BookingId} " +
                    $"was cancelled. The {message.Seats} reserved seat(s) were released."
            }, cancellationToken);
    }
}
