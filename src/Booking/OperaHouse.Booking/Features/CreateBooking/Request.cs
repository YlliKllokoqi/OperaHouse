namespace OperaHouse.Booking.Features.CreateBooking;

public sealed record CreateBookingInput(
    Guid IdempotencyKey,
    Guid PerformanceId,
    string CustomerEmail,
    int Seats);
