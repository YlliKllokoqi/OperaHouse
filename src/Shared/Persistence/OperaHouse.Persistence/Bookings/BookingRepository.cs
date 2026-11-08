using OperaHouse.Booking.Persistence;
using OperaHouse.Booking.Features.ExpireBookings;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using OperaHouse.Booking.Models;
using OperaHouse.Performance.Models;
using OperaHouse.Persistence.Outbox;
using OperaHouse.Contracts.Events;
using OperaHouse.Messaging;
using BookingEntity = OperaHouse.Booking.Models.Booking;

namespace OperaHouse.Persistence.Bookings;

public sealed class BookingRepository(
    BookingDbContext dbContext,
    IOptions<RabbitMqOptions> rabbitMqOptions) : IBookingRepository
{
    public Task<BookingEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Bookings.AsNoTracking().SingleOrDefaultAsync(booking => booking.Id == id, cancellationToken);

    private const string IdempotencyConstraintName = "UX_Bookings_IdempotencyKey";
    private readonly RabbitMqOptions _rabbitMqOptions = rabbitMqOptions.Value;
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
                booking.CustomerEmail,
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

        await ReleaseSeatsAsync(booking.BookingId, booking.PerformanceId, booking.Seats, currentTime, cancellationToken);

        await dbContext.OutboxMessages.AddAsync(
            CreateOutboxMessage(
                bookingExpired.MessageId,
                bookingExpired.CorrelationId,
                nameof(BookingExpired),
                _rabbitMqOptions.BookingExpiredRoutingKey,
                bookingExpired.OccurredAt,
                bookingExpired),
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    public async Task<BookingCancellationPersistenceResult> TryCancelAsync(Guid id, DateTimeOffset currentTime,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var updatedRows = await dbContext.Bookings
            .Where(booking => booking.Id == id
                && booking.Status == BookingStatus.Pending
                && booking.ExpiresAt > currentTime)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(booking => booking.Status, BookingStatus.Cancelled)
                .SetProperty(booking => booking.CancelledAt, currentTime), cancellationToken);

        var booking = await GetByIdAsync(id, cancellationToken);
        if (updatedRows == 0)
        {
            return booking switch
            {
                null => new(BookingCancellationOutcome.NotFound),
                { Status: BookingStatus.Cancelled } => new(BookingCancellationOutcome.Cancelled, booking),
                _ => new(BookingCancellationOutcome.Ineligible)
            };
        }

        if (booking is null)
        {
            throw new InvalidOperationException($"Booking {id} disappeared during cancellation.");
        }

        await ReleaseSeatsAsync(id, booking.PerformanceId, booking.Seats, currentTime, cancellationToken);

        var message = new BookingCancelled(Guid.NewGuid(), Guid.NewGuid(), id, booking.PerformanceId,
            booking.CustomerEmail, booking.Seats, currentTime);
        await dbContext.OutboxMessages.AddAsync(
            CreateOutboxMessage(message.MessageId, message.CorrelationId, nameof(BookingCancelled),
                _rabbitMqOptions.BookingCancelledRoutingKey, message.OccurredAt, message), cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(BookingCancellationOutcome.Cancelled, booking);
    }

    private async Task ReleaseSeatsAsync(Guid bookingId, Guid performanceId, int seats, DateTimeOffset currentTime,
        CancellationToken cancellationToken)
    {
        var releasedPerformanceRows =
            await dbContext.Performances
                .Where(performance =>
                    performance.Id == performanceId
                    && performance.AvailableSeats
                    + seats
                    <= performance.Capacity)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(
                        performance =>
                            performance.AvailableSeats,
                        performance =>
                            performance.AvailableSeats + seats)
                    .SetProperty(
                        performance => performance.UpdatedAt,
                        currentTime), cancellationToken);

        if (releasedPerformanceRows != 1)
        {
            throw new InvalidOperationException($"Could not release seats for booking {bookingId}");
        }
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
