namespace OperaHouse.Booking.Domain.Bookings;

public sealed class BookingOptions
{
    public const string SectionName = "Booking";
    public int ReservationDurationMinutes { get; init; } = 15;
    public int ExpirationCheckIntervalSeconds { get; init; } = 30;
    public int ExpirationBatchSize { get; init; } = 100;
}