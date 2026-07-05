using OperaHouse.Notification.Application.Notifications;

namespace OperaHouse.Notification.Infrastructure.Persistence;

public sealed class NotificationUnitOfWork(NotificationDbContext dbContext)
    : INotificationUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
