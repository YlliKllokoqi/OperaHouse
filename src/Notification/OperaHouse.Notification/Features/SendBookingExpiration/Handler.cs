using OperaHouse.Contracts.Events;
using OperaHouse.Notification.Domain.Notifications;
using OperaHouse.Notification.Processing;

namespace OperaHouse.Notification.Features;

public sealed class SendBookingExpiration(NotificationProcessor processor)
{
    public Task<NotificationProcessingResult> HandleAsync(
        BookingExpired message,
        CancellationToken cancellationToken)
    {
        BookingEventValidator.Validate(message);

        return processor.ProcessAsync(message.MessageId,
            "OperaHouse.Notification.BookingExpired", nameof(BookingExpired),
            new NotificationMessage
            {
                Type = "BookingExpiration",
                Recipient = message.CustomerEmail,
                Subject = "Your OperaHouse reservation expired",
                Body = $"Your reservation for booking {message.BookingId} " +
                    $"expired. The {message.Seats} reserved seat(s) were released."
            }, cancellationToken);
    }
}
