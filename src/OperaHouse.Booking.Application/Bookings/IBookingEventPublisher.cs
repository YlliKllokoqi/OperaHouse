using BookingEntity = OperaHouse.Booking.Domain.Bookings.Booking;

namespace OperaHouse.Booking.Application.Bookings;

public interface IBookingEventPublisher
{
    Task PublishBookingCreatedAsync(
        BookingEntity booking,
        CancellationToken cancellationToken);
}