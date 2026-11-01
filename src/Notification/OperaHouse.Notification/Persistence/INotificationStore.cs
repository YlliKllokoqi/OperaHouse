using OperaHouse.Notification.Domain.Inbox;
using OperaHouse.Notification.Domain.Notifications;

namespace OperaHouse.Notification.Persistence;

public interface INotificationStore
{
    Task<InboxMessage?> GetInboxMessageAsync(Guid messageId, string consumer, CancellationToken cancellationToken);
    Task<NotificationMessage?> GetNotificationAsync(Guid inboxMessageId, CancellationToken cancellationToken);
    Task AddInboxMessageAsync(InboxMessage message, CancellationToken cancellationToken);
    Task AddNotificationAsync(NotificationMessage message, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
