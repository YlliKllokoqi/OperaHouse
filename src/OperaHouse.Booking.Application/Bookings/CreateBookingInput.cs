namespace OperaHouse.Booking.Application.Bookings;

public sealed record CreateBookingInput(
    Guid PerformanceId,
    string CustomerEmail,
    int Seats);
