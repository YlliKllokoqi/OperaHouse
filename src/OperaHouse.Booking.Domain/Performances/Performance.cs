namespace OperaHouse.Booking.Domain.Performances;

public sealed class Performance
{
    private Performance()
    {
    }

    public Performance(
        string title,
        string venue,
        DateTimeOffset startsAt,
        int capacity,
        DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        Title = title.Trim();
        Venue = venue.Trim();
        Capacity = capacity;
        StartsAt = startsAt;
        AvailableSeats = capacity;
        Status = PerformanceStatus.Draft;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }
    public Guid Id { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Venue { get; private set; } = string.Empty;
    public PerformanceStatus Status { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }
    public int AvailableSeats { get; private set; }
    public int Capacity { get; private set; }

    public bool UpdateDraft(
        string title,
        string venue,
        DateTimeOffset startsAt,
        int capacity,
        DateTimeOffset updatedAt)
    {
        if (Status != PerformanceStatus.Draft)
        {
            return false;
        }

        Title = title.Trim();
        Venue = venue.Trim();
        Capacity = capacity;
        StartsAt = startsAt;
        AvailableSeats = capacity;
        UpdatedAt = updatedAt;

        return true;
    }

    public bool Publish(DateTimeOffset publishedAt)
    {
        if (Status != PerformanceStatus.Draft)
        {
            return false;
        }

        if (StartsAt <= publishedAt)
        {
            return false;
        }

        PublishedAt = publishedAt;
        UpdatedAt = publishedAt;
        Status = PerformanceStatus.Published;

        return true;
    }

    public bool Cancel(string reason, DateTimeOffset cancelledAt)
    {
        if (Status is PerformanceStatus.Cancelled or PerformanceStatus.Completed)
        {
            return false;
        }

        Status = PerformanceStatus.Cancelled;
        CancellationReason = reason.Trim();
        CancelledAt = cancelledAt;
        UpdatedAt = cancelledAt;

        return true;
    }

    public bool CanBeBooked(DateTimeOffset currentTime)
    {
        return Status == PerformanceStatus.Published
               && StartsAt > currentTime
               && AvailableSeats > 0;
    }
}
