namespace OperaHouse.Booking.Application.Bookings;

public sealed record ExpiringBooking(Guid BookingId, Guid PerformanceId, string CustomerEmail, int Seats);