namespace OperaHouse.Booking.Models;

public sealed record BookingDto(
    Guid Id,
    Guid PerformanceId,
    string CustomerEmail,
    int Seats,
    BookingStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? ExpiredAt,
    DateTimeOffset? CancelledAt)
{
    public static BookingDto From(Booking booking) => new(
        booking.Id, booking.PerformanceId, booking.CustomerEmail, booking.Seats, booking.Status,
        booking.CreatedAt, booking.ExpiresAt, booking.ConfirmedAt, booking.ExpiredAt, booking.CancelledAt);
}
