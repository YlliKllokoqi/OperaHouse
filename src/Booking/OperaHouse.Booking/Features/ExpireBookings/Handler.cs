using OperaHouse.Booking.Persistence;
using OperaHouse.Contracts.Events;

namespace OperaHouse.Booking.Features.ExpireBookings;

public sealed class Handler(IBookingRepository repository, TimeProvider timeProvider)
{
    public Task<bool> HandleAsync(ExpiringBooking booking, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var message = new BookingExpired(Guid.NewGuid(), Guid.NewGuid(), booking.BookingId,
            booking.PerformanceId, booking.CustomerEmail, booking.Seats, now);
        return repository.TryExpireAsync(booking, message, now, cancellationToken);
    }
}
