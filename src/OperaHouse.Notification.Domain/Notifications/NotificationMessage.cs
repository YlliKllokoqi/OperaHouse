namespace OperaHouse.Notification.Domain.Notifications;

public sealed class NotificationMessage
{
    public Guid Id { get; set; }

    public Guid InboxMessageId { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Recipient { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public DateTimeOffset? FailedAt { get; set; }

    public string? FailureReason { get; set; }
}
