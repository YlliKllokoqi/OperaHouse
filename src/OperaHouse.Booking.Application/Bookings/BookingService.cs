using OperaHouse.Booking.Application.Performances;
using OperaHouse.Booking.Domain.Bookings;
using OperaHouse.Contracts.Events;
using BookingEntity = OperaHouse.Booking.Domain.Bookings.Booking;

namespace OperaHouse.Booking.Application.Bookings;

public sealed class BookingService(
    IBookingRepository bookingRepository,
    IPerformanceRepository performanceRepository)
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

        var messageId = Guid.NewGuid();

        var bookingCreated = new BookingCreated(
            MessageId: messageId,
            CorrelationId: Guid.NewGuid(),
            BookingId: booking.Id,
            PerformanceId: booking.PerformanceId,
            CustomerEmail: booking.CustomerEmail,
            Seats: booking.Seats,
            OccurredAt: DateTimeOffset.UtcNow);
        
        await bookingRepository.AddAsync(
            booking,
            bookingCreated,
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
