using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OperaHouse.Booking.Application.Bookings;
using OperaHouse.Booking.Infrastructure.Persistence;
using OperaHouse.Booking.Infrastructure.Persistence.Outbox;
using OperaHouse.Contracts.Events;
using BookingEntity = OperaHouse.Booking.Domain.Bookings.Booking;

namespace OperaHouse.Booking.Infrastructure.Bookings;

public sealed class BookingRepository(BookingDbContext dbContext) : IBookingRepository
{
    public async Task<BookingEntity?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return await dbContext.Bookings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                booking => booking.Id == id,
                cancellationToken);
    }

    public async Task AddAsync(
        BookingEntity booking,
        BookingCreated bookingCreated,
        CancellationToken cancellationToken)
    {
        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            MessageId = bookingCreated.MessageId,
            CorrelationId = bookingCreated.CorrelationId,
            Type = nameof(BookingCreated),
            RoutingKey = "booking.created",
            Payload = JsonSerializer.Serialize(bookingCreated),
            OccurredAt = bookingCreated.OccurredAt
        };
        
        await dbContext.Bookings.AddAsync(
            booking,
            cancellationToken);

        await dbContext.OutboxMessages.AddAsync(
            outboxMessage,
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
