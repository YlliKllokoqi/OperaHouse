using Microsoft.EntityFrameworkCore;
using OperaHouse.Notification.Application.Notifications;
using OperaHouse.Notification.Domain.Notifications;
using OperaHouse.Notification.Infrastructure.Persistence;

namespace OperaHouse.Notification.Infrastructure.Notifications;

public sealed class NotificationRepository(NotificationDbContext dbContext)
    : INotificationRepository
{
    public Task<NotificationMessage?> GetByInboxMessageIdAsync(
        Guid inboxMessageId,
        CancellationToken cancellationToken)
    {
        return dbContext.NotificationMessages
            .SingleOrDefaultAsync(
                notification => notification.InboxMessageId == inboxMessageId,
                cancellationToken);
    }

    public async Task AddAsync(
        NotificationMessage notification,
        CancellationToken cancellationToken)
    {
        await dbContext.NotificationMessages.AddAsync(
            notification,
            cancellationToken);
    }
}
