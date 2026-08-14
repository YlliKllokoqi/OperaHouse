namespace OperaHouse.Booking.Application.Bookings;

public enum BookingCreationOutcome
{
    Created,
    Existing,
    PerformanceUnavailable,
    IdempotencyConflict
}