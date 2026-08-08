using AutoMapper;
using FluentValidation;
using OperaHouse.Booking.Application.Common;
using OperaHouse.Booking.Application.Performances;
using OperaHouse.Booking.Domain.Bookings;
using OperaHouse.Contracts.Events;
using BookingEntity = OperaHouse.Booking.Domain.Bookings.Booking;

namespace OperaHouse.Booking.Application.Bookings;

public sealed class BookingService(
    IBookingRepository bookingRepository,
    IPerformanceRepository performanceRepository,
    IValidator<CreateBookingInput> inputValidator,
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

        var performance = await performanceRepository.GetByIdAsync(
            input.PerformanceId,
            cancellationToken);

        var currentTime = timeProvider.GetUtcNow();

        if (performance is null || !performance.CanBeBooked(currentTime))
        {
            return ApplicationResult<BookingDto>.Failure(
                new ApplicationError(
                    "performance.unavailable",
                    "Performance was not found or is unavailable.",
                    ApplicationErrorType.NotFound));
        }

        var booking = new BookingEntity
        {
            Id = Guid.NewGuid(),
            PerformanceId = input.PerformanceId,
            CustomerEmail = input.CustomerEmail.Trim(),
            Seats = input.Seats,
            Status = BookingStatus.Pending,
            CreatedAt = currentTime
        };

        var messageId = Guid.NewGuid();

        var bookingCreated = new BookingCreated(
            MessageId: messageId,
            CorrelationId: Guid.NewGuid(),
            BookingId: booking.Id,
            PerformanceId: booking.PerformanceId,
            CustomerEmail: booking.CustomerEmail,
            Seats: booking.Seats,
            OccurredAt: currentTime);
        
        await bookingRepository.AddAsync(
            booking,
            bookingCreated,
            cancellationToken);

        return ApplicationResult<BookingDto>.Success(
            mapper.Map<BookingDto>(booking));
    }
}
