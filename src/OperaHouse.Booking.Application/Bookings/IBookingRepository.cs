using OperaHouse.Contracts.Events;
using BookingEntity = OperaHouse.Booking.Domain.Bookings.Booking;

namespace OperaHouse.Booking.Application.Bookings;

public interface IBookingRepository
{
    Task<BookingEntity?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<BookingCreationPersistenceResult> TryCreateAsync(
        BookingEntity booking,
        BookingCreated bookingCreated,
        DateTimeOffset currentTime,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ExpiringBooking>> GetExpiringBookingAsync(
        DateTimeOffset currentTime,
        int batchSize,
        CancellationToken cancellationToken);

    Task<bool> TryExpireAsync(
        ExpiringBooking booking,
        BookingExpired bookingExpired,
        DateTimeOffset currentTime,
        CancellationToken cancellationToken);
}
