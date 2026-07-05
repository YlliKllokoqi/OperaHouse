using OperaHouse.Notification.Domain.Notifications;

namespace OperaHouse.Notification.Application.Notifications;

public interface INotificationRepository
{
    Task<NotificationMessage?> GetByInboxMessageIdAsync(
        Guid inboxMessageId,
        CancellationToken cancellationToken);

    Task AddAsync(
        NotificationMessage notification,
        CancellationToken cancellationToken);
}
