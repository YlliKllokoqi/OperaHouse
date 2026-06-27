using OperaHouse.Booking.Application.Performances;
using OperaHouse.Booking.Domain.Bookings;
using BookingEntity = OperaHouse.Booking.Domain.Bookings.Booking;

namespace OperaHouse.Booking.Application.Bookings;

public sealed class BookingService(
    IBookingRepository bookingRepository,
    IPerformanceRepository performanceRepository,
    IBookingEventPublisher bookingEventPublisher)
    : IBookingService
{
    public async Task<BookingDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var booking = await bookingRepository.GetByIdAsync(
            id,
            cancellationToken);

        return booking is null
            ? null
            : ToDto(booking);
    }

    public async Task<BookingDto?> CreateAsync(
        Guid performanceId,
        string customerEmail,
        int seats,
        CancellationToken cancellationToken)
    {
        var performance = await performanceRepository.GetByIdAsync(
            performanceId,
            cancellationToken);

        if (performance is null)
        {
            return null;
        }

        var booking = new BookingEntity
        {
            Id = Guid.NewGuid(),
            PerformanceId = performanceId,
            CustomerEmail = customerEmail,
            Seats = seats,
            Status = BookingStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await bookingRepository.AddAsync(
            booking,
            cancellationToken);

        await bookingEventPublisher.PublishBookingCreatedAsync(
            booking,
            cancellationToken);

        return ToDto(booking);
    }

    private static BookingDto ToDto(BookingEntity booking)
    {
        return new BookingDto(
            booking.Id,
            booking.PerformanceId,
            booking.CustomerEmail,
            booking.Seats,
            booking.Status,
            booking.CreatedAt);
    }
}
