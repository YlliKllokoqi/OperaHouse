using BookingEntity = OperaHouse.Booking.Domain.Bookings.Booking;

namespace OperaHouse.Booking.Application.Bookings;

public interface IBookingRepository
{
    Task<BookingEntity?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task AddAsync(
        BookingEntity booking,
        CancellationToken cancellationToken);
}
