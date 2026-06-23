using OperaHouse.Booking.Domain.Bookings;

namespace OperaHouse.Booking.Application.Bookings;

public sealed record BookingDto(
    Guid Id,
    Guid PerformanceId,
    string CustomerEmail,
    int Seats,
    BookingStatus Status,
    DateTimeOffset CreatedAt);
