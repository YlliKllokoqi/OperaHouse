using Microsoft.Extensions.Options;
using OperaHouse.Booking.Application.Bookings;
using OperaHouse.Booking.Domain.Performances;
using OperaHouse.Contracts.Events;
using OperaHouse.Messaging;

namespace OperaHouse.Booking.Infrastructure.Bookings;

public class RabbitMqBookingEventPublisher(
    RabbitMqPublisher rabbitMqPublisher,
    IOptions<RabbitMqOptions> options) : IBookingEventPublisher
{
    private readonly RabbitMqOptions _options = options.Value;
    
    public async Task PublishBookingCreatedAsync(Domain.Bookings.Booking booking, CancellationToken cancellationToken)
    {
        var message = new BookingCreated(
            MessageId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            BookingId: booking.Id,
            PerformanceId: booking.PerformanceId,
            CustomerEmail: booking.CustomerEmail,
            Seats: booking.Seats,
            OccuredAt: DateTimeOffset.UtcNow
        );

        await rabbitMqPublisher.PublishAsync(
            message,
            _options.BookingCreatedRoutingKey,
            cancellationToken);
    }
}