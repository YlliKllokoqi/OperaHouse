using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using OperaHouse.Booking.Application.Bookings;
using OperaHouse.Booking.Domain.Bookings;
using OperaHouse.Booking.Domain.Performances;
using OperaHouse.Booking.Infrastructure.Persistence;
using OperaHouse.Booking.Infrastructure.Persistence.Outbox;
using OperaHouse.Contracts.Events;
using OperaHouse.Messaging;
using BookingEntity = OperaHouse.Booking.Domain.Bookings.Booking;

namespace OperaHouse.Booking.Infrastructure.Bookings;

public sealed class BookingRepository(
    BookingDbContext dbContext,
    IOptions<RabbitMqOptions> rabbitMqOptions) : IBookingRepository
{
    private const string IdempotencyConstraintName = "UX_Bookings_IdempotencyKey";
    private readonly RabbitMqOptions _rabbitMqOptions = rabbitMqOptions.Value;
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

    public async Task<BookingCreationPersistenceResult> TryCreateAsync(
        BookingEntity booking,
        BookingCreated bookingCreated,
        DateTimeOffset currentTime,
        CancellationToken cancellationToken)
    {
        var existingBooking = await FindByIdempotencyKeyAsync(
            booking.IdempotencyKey,
            cancellationToken);

        if (existingBooking is not null)
        {
            return CreateIdempotencyResult(existingBooking, booking);
        }

        await using var transaction = await dbContext
            .Database
            .BeginTransactionAsync(cancellationToken);

        try
        {
            var updatedRows = await dbContext.Performances
                .Where(performance =>
                    performance.Id == booking.PerformanceId
                    && performance.Status
                    == PerformanceStatus.Published
                    && performance.StartsAt > currentTime
                    && performance.AvailableSeats >= booking.Seats)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            performance =>
                                performance.AvailableSeats,
                            performance =>
                                performance.AvailableSeats - booking.Seats)
                        .SetProperty(performance =>
                            performance.UpdatedAt, currentTime),
                    cancellationToken);

            if (updatedRows == 0)
            {
                await transaction.RollbackAsync(cancellationToken);

                return new BookingCreationPersistenceResult(
                    BookingCreationOutcome.PerformanceUnavailable);
            }

            await dbContext.Bookings.AddAsync(booking, cancellationToken);

            await dbContext.OutboxMessages.AddAsync(
                CreateOutboxMessage(
                    bookingCreated.MessageId,
                    bookingCreated.CorrelationId,
                    nameof(bookingCreated),
                    _rabbitMqOptions.BookingCreatedRoutingKey,
                    bookingCreated.OccurredAt,
                    bookingCreated),
                cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new BookingCreationPersistenceResult(
                BookingCreationOutcome.Created,
                booking);
        }
        catch (DbUpdateException e)
        when(IsIdempotencyConflict(e))
        {
            await transaction.RollbackAsync(cancellationToken);
            
            dbContext.ChangeTracker.Clear();

            var concurrentBooking =
                await FindByIdempotencyKeyAsync(
                    booking.IdempotencyKey,
                    cancellationToken);

            if (concurrentBooking is null)
            {
                throw new InvalidOperationException(
                    "An idempotency conflict ocurred, but the existing booking could not be loaded",
                    e);
            }
            
            return CreateIdempotencyResult(
                concurrentBooking,
                booking);
        }
    }

    public async Task<IReadOnlyList<ExpiringBooking>> GetExpiringBookingAsync(
        DateTimeOffset currentTime,
        int batchSize,
        CancellationToken cancellationToken)
    {
        return await dbContext.Bookings
            .AsNoTracking()
            .Where(booking =>
                booking.Status == BookingStatus.Pending
                && booking.ExpiresAt <= currentTime)
            .OrderBy(booking => booking.ExpiresAt)
            .Take(batchSize)
            .Select(booking => new ExpiringBooking(
                booking.Id,
                booking.PerformanceId,
                booking.Seats))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryExpireAsync(ExpiringBooking booking,
        BookingExpired bookingExpired,
        DateTimeOffset currentTime,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var expiredRows = await dbContext.Bookings
            .Where(existingBooking =>
                existingBooking.Id == booking.BookingId
                && existingBooking.Status == BookingStatus.Pending
                && existingBooking.ExpiresAt <= currentTime)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        existingBooking => existingBooking.Status,
                        BookingStatus.Expired)
                    .SetProperty(
                        existingBooking => existingBooking.ExpiredAt,
                        currentTime),
                cancellationToken);

        if (expiredRows == 0)
        {
            await transaction.RollbackAsync(cancellationToken);

            return false;
        }

        var releasedPerformanceRows =
            await dbContext.Performances
                .Where(performance =>
                    performance.Id == booking.PerformanceId
                    && performance.AvailableSeats
                    + booking.Seats
                    <= performance.Capacity)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(
                        performance =>
                            performance.AvailableSeats,
                        performance =>
                            performance.AvailableSeats + booking.Seats)
                    .SetProperty(
                        performance => performance.UpdatedAt,
                        currentTime), cancellationToken);

        if (releasedPerformanceRows != 1)
        {
            throw new InvalidOperationException($"Could not release seats for booking {booking.BookingId}");
        }

        await dbContext.OutboxMessages.AddAsync(
            CreateOutboxMessage(
                bookingExpired.MessageId,
                bookingExpired.CorrelationId,
                nameof(BookingExpired),
                _rabbitMqOptions.BookingExpiredRoutingKey,
                bookingExpired.OccuredAt,
                bookingExpired),
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    private async Task<BookingEntity?> FindByIdempotencyKeyAsync(
        Guid idempotencyKey,
        CancellationToken cancellationToken)
    {
        return await dbContext.Bookings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                booking => booking.IdempotencyKey == idempotencyKey,
                cancellationToken);
    }

    private static BookingCreationPersistenceResult CreateIdempotencyResult(
        BookingEntity existingBooking,
        BookingEntity requestedBooking)
    {
        var representsSameRequest =
            existingBooking.HasSameReservationDetails(
                requestedBooking.PerformanceId,
                requestedBooking.CustomerEmail,
                requestedBooking.Seats);

        return representsSameRequest
            ? new BookingCreationPersistenceResult(
                BookingCreationOutcome.Existing, existingBooking)
            : new BookingCreationPersistenceResult(BookingCreationOutcome.IdempotencyConflict);
    }

    private static bool IsIdempotencyConflict(
        DbUpdateException exception)
    {
        return exception.InnerException
            is PostgresException
            {
                SqlState:
                    PostgresErrorCodes.UniqueViolation,
                ConstraintName:
                    IdempotencyConstraintName
            };
    }

    private static OutboxMessage CreateOutboxMessage<TMessage>(
        Guid messageId,
        Guid correlationId,
        string type,
        string routingKey,
        DateTimeOffset ocurredAt,
        TMessage message)
    {
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            MessageId = messageId,
            CorrelationId = correlationId,
            Type = type,
            RoutingKey = routingKey,
            Payload = JsonSerializer.Serialize(message),
            OccurredAt = ocurredAt
        };
    }
}
