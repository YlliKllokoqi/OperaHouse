using Microsoft.EntityFrameworkCore;
using OperaHouse.Notification.Application.Inbox;
using OperaHouse.Notification.Domain.Inbox;
using OperaHouse.Notification.Infrastructure.Persistence;

namespace OperaHouse.Notification.Infrastructure.Inbox;

public sealed class InboxRepository(NotificationDbContext dbContext)
    : IInboxRepository
{
    public Task<InboxMessage?> GetByMessageAsync(
        Guid messageId,
        string consumer,
        CancellationToken cancellationToken)
    {
        return dbContext.InboxMessages
            .SingleOrDefaultAsync(
                inboxMessage =>
                    inboxMessage.MessageId == messageId &&
                    inboxMessage.Consumer == consumer,
                cancellationToken);
    }

    public async Task AddAsync(
        InboxMessage inboxMessage,
        CancellationToken cancellationToken)
    {
        await dbContext.InboxMessages.AddAsync(
            inboxMessage,
            cancellationToken);
    }
}
