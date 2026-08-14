namespace OperaHouse.Booking.Application.Bookings;

public sealed record CreateBookingInput(
    Guid IdempotencyKey,
    Guid PerformanceId,
    string CustomerEmail,
    int Seats);
