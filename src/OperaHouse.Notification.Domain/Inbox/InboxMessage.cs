namespace OperaHouse.Notification.Domain.Inbox;

public sealed class InboxMessage
{
    public Guid Id { get; set; }

    public Guid MessageId { get; set; }

    public string MessageType { get; set; } = string.Empty;

    public string Consumer { get; set; } = string.Empty;

    public InboxMessageStatus Status { get; set; } = InboxMessageStatus.Processing;

    public DateTimeOffset ReceivedAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public string? LastError { get; set; }
}
