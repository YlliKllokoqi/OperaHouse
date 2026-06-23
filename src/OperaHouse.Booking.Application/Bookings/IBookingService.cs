namespace OperaHouse.Booking.Application.Bookings;

public interface IBookingService
{
    Task<BookingDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<BookingDto?> CreateAsync(
        Guid performanceId,
        string customerEmail,
        int seats,
        CancellationToken cancellationToken);
}
