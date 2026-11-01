using Microsoft.EntityFrameworkCore;
using OperaHouse.Notification.Domain.Inbox;
using OperaHouse.Notification.Domain.Notifications;
using OperaHouse.Notification.Persistence;

namespace OperaHouse.Notification.Persistence;

public sealed class NotificationStore(NotificationDbContext dbContext) : INotificationStore
{
    public Task<InboxMessage?> GetInboxMessageAsync(Guid messageId, string consumer, CancellationToken cancellationToken) =>
        dbContext.InboxMessages.SingleOrDefaultAsync(
            message => message.MessageId == messageId && message.Consumer == consumer,
            cancellationToken);

    public Task<NotificationMessage?> GetNotificationAsync(Guid inboxMessageId, CancellationToken cancellationToken) =>
        dbContext.NotificationMessages.SingleOrDefaultAsync(
            message => message.InboxMessageId == inboxMessageId, cancellationToken);

    public async Task AddInboxMessageAsync(InboxMessage message, CancellationToken cancellationToken) =>
        await dbContext.InboxMessages.AddAsync(message, cancellationToken);

    public async Task AddNotificationAsync(NotificationMessage message, CancellationToken cancellationToken) =>
        await dbContext.NotificationMessages.AddAsync(message, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
