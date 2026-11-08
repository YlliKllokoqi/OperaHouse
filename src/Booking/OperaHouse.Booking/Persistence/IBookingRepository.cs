using OperaHouse.Booking.Features.ExpireBookings;
using OperaHouse.Contracts.Events;
using BookingEntity = OperaHouse.Booking.Models.Booking;

namespace OperaHouse.Booking.Persistence;

public interface IBookingRepository
{
    Task<BookingEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<BookingCreationPersistenceResult> TryCreateAsync(BookingEntity booking, BookingCreated message,
        DateTimeOffset currentTime, CancellationToken cancellationToken);
    Task<IReadOnlyList<ExpiringBooking>> GetExpiringBookingAsync(DateTimeOffset currentTime,
        int batchSize, CancellationToken cancellationToken);
    Task<bool> TryExpireAsync(ExpiringBooking booking, BookingExpired message,
        DateTimeOffset currentTime, CancellationToken cancellationToken);
    Task<BookingCancellationPersistenceResult> TryCancelAsync(Guid id, DateTimeOffset currentTime,
        CancellationToken cancellationToken);
}
