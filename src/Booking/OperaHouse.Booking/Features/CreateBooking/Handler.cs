using OperaHouse.Booking.Configuration;
using OperaHouse.Booking.Models;
using OperaHouse.Booking.Persistence;
using FluentValidation;
using Microsoft.Extensions.Options;
using OperaHouse.Contracts.Events;
using OperaHouse.Contracts.Results;
using BookingEntity = OperaHouse.Booking.Models.Booking;

namespace OperaHouse.Booking.Features.CreateBooking;

public sealed class Handler(
    IBookingRepository bookingRepository,
    IValidator<CreateBookingInput> inputValidator,
    IOptions<BookingOptions> bookingOptions,
    TimeProvider timeProvider)
{
    public async Task<ApplicationResult<BookingDto>> HandleAsync(
        CreateBookingInput input,
        CancellationToken cancellationToken)
    {
        var validationResult = await inputValidator.ValidateAsync(
            input,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return ApplicationResult<BookingDto>.Failure(
                ApplicationError.FromValidation("booking.validation",
                    "The booking request is invalid.",
                    validationResult.Errors.Select(error => (error.PropertyName, error.ErrorMessage))));
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
                    BookingDto.From(persistenceResult.Booking!)),

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
