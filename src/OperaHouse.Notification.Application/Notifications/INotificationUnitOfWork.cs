namespace OperaHouse.Notification.Application.Notifications;

public interface INotificationUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
