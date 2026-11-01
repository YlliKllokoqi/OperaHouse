namespace OperaHouse.Booking.Persistence;

public enum BookingCreationOutcome
{
    Created,
    Existing,
    PerformanceUnavailable,
    IdempotencyConflict
}
