using OperaHouse.Booking.Application.Common;

namespace OperaHouse.Booking.Application.Bookings;

public interface IBookingService
{
    Task<ApplicationResult<BookingDto>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<ApplicationResult<BookingDto>> CreateAsync(
        CreateBookingInput input,
        CancellationToken cancellationToken);
}
