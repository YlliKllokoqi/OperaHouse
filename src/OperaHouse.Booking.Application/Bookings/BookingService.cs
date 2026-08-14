using AutoMapper;
using FluentValidation;
using Microsoft.Extensions.Options;
using OperaHouse.Booking.Application.Common;
using OperaHouse.Booking.Application.Performances;
using OperaHouse.Booking.Domain.Bookings;
using OperaHouse.Contracts.Events;
using BookingEntity = OperaHouse.Booking.Domain.Bookings.Booking;

namespace OperaHouse.Booking.Application.Bookings;

public sealed class BookingService(
    IBookingRepository bookingRepository,
    IValidator<CreateBookingInput> inputValidator,
    IOptions<BookingOptions> bookingOptions,
    TimeProvider timeProvider,
    IMapper mapper)
    : IBookingService
{
    public async Task<ApplicationResult<BookingDto>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var booking = await bookingRepository.GetByIdAsync(
            id,
            cancellationToken);

        if (booking is null)
        {
            return ApplicationResult<BookingDto>.Failure(
                new ApplicationError(
                    "booking.not-found",
                    "Booking was not found.",
                    ApplicationErrorType.NotFound));
        }

        return ApplicationResult<BookingDto>.Success(
            mapper.Map<BookingDto>(booking));
    }

    public async Task<ApplicationResult<BookingDto>> CreateAsync(
        CreateBookingInput input,
        CancellationToken cancellationToken)
    {
        var validationResult = await inputValidator.ValidateAsync(
            input,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return ApplicationResult<BookingDto>.Failure(
                validationResult.ToApplicationError(
                    "booking.validation",
                    "The booking request is invalid."));
        }

        var currentTime = timeProvider.GetUtcNow();
        var expiresAt = currentTime
            .AddMinutes(bookingOptions
                .Value
                .ReservationDurationMinutes);

        var booking = new BookingEntity(
            id: Guid.NewGuid(),
            idempotencyKey: input.IdempotencyKey,
            performanceId: input.PerformanceId,
            customerEmail: input.CustomerEmail,
            seats: input.Seats,
            createdAt: currentTime,
            expiresAt: expiresAt);
        
        var bookingCreated = new BookingCreated(
            MessageId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            BookingId: booking.Id,
            PerformanceId: booking.PerformanceId,
            CustomerEmail: booking.CustomerEmail,
            Seats: booking.Seats,
            ExpiresAt: booking.ExpiresAt,
            OccurredAt: currentTime);

        var persistenceResult = await bookingRepository.TryCreateAsync(
            booking,
            bookingCreated,
            currentTime,
            cancellationToken);

        return persistenceResult.Outcome switch
        {
            BookingCreationOutcome.Created
                or BookingCreationOutcome.Existing =>
                ApplicationResult<BookingDto>.Success(
                    mapper.Map<BookingDto>(persistenceResult.Booking!)),

            BookingCreationOutcome.PerformanceUnavailable =>
                ApplicationResult<BookingDto>.Failure(
                    new ApplicationError(
                        "performance.unavailable",
                        "the performance is unavailable or does not have enough available seats.",
                        ApplicationErrorType.Conflict
                    )),

            BookingCreationOutcome.IdempotencyConflict =>
                ApplicationResult<BookingDto>.Failure(
                    new ApplicationError(
                        "booking.idempotency-conflict",
                        "The Idempotency-Key was already used for a different booking request.",
                        ApplicationErrorType.Conflict)),

            _ => throw new InvalidOperationException("Unknown booking creation outcome.")
        };
    }
}
