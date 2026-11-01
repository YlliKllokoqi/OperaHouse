namespace OperaHouse.Booking.Features.ExpireBookings;

public sealed record ExpiringBooking(Guid BookingId, Guid PerformanceId, string CustomerEmail, int Seats);
