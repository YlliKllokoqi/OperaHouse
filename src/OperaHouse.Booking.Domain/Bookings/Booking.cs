namespace OperaHouse.Booking.Domain.Bookings;

public class Booking
{
    private Booking()
    {
        
    }

    public Booking(
        Guid id,
        Guid idempotencyKey,
        Guid performanceId,
        string customerEmail,
        int seats,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Booking ID cannot be empty.",
                nameof(id));
        }

        if (idempotencyKey == Guid.Empty)
        {
            throw new ArgumentException(
                "Idempotency key cannot be empty.",
                nameof(idempotencyKey));
        }

        if (performanceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Performance ID cannot be empty.",
                nameof(performanceId));
        }

        if (string.IsNullOrWhiteSpace(customerEmail))
        {
            throw new ArgumentException(
                "Customer email is required.",
                nameof(customerEmail));
        }

        if (seats <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seats),
                "Seat count must be greater than zero.");
        }

        if (expiresAt <= createdAt)
        {
            throw new ArgumentException(
                "Expiration must be later than creation time.",
                nameof(expiresAt));
        }

        Id = id;
        IdempotencyKey = idempotencyKey;
        PerformanceId = performanceId;
        CustomerEmail = customerEmail;
        Seats = seats;
        Status = BookingStatus.Pending;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }
    public Guid Id { get; private set; }
    public Guid IdempotencyKey { get; private set; }
    public Guid PerformanceId { get; private set; }
    public string CustomerEmail { get; private set; } = String.Empty;
    public int Seats { get; private set; }
    public BookingStatus Status { get; private set; } = BookingStatus.Pending;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? ExpiredAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    public bool HasSameReservationDetails(Guid performanceId, string customerEmail, int seats)
    {
        return PerformanceId == performanceId
               && Seats == seats
               && string.Equals(
                   CustomerEmail,
                   customerEmail.Trim(),
                   StringComparison.OrdinalIgnoreCase);
    }
}