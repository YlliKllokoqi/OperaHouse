using OperaHouse.Notification.Domain.Inbox;

namespace OperaHouse.Notification.Application.Inbox;

public interface IInboxRepository
{
    Task<InboxMessage?> GetByMessageAsync(
        Guid messageId,
        string consumer,
        CancellationToken cancellationToken);

    Task AddAsync(
        InboxMessage inboxMessage,
        CancellationToken cancellationToken);
}
