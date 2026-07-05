using OperaHouse.Contracts.Events;

namespace OperaHouse.Notification.Application.Notifications;

public interface INotificationProcessor
{
    Task<NotificationProcessingResult> ProcessBookingCreatedAsync(
        BookingCreated message,
        CancellationToken cancellationToken);
}
