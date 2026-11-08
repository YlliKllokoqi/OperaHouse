using OperaHouse.Booking.Models;
using OperaHouse.Booking.Persistence;
using OperaHouse.Contracts.Results;

namespace OperaHouse.Booking.Features.CancelBooking;

public sealed class Handler(IBookingRepository repository, TimeProvider timeProvider)
{
    public async Task<ApplicationResult<BookingDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            return ApplicationResult<BookingDto>.Failure(ApplicationError.FromValidation(
                "booking.cancellation-validation", "The cancellation request is invalid.",
                [("Id", "Booking ID is required.")]));
        }

        var result = await repository.TryCancelAsync(id, timeProvider.GetUtcNow(), cancellationToken);
        return result.Outcome switch
        {
            BookingCancellationOutcome.Cancelled => ApplicationResult<BookingDto>.Success(BookingDto.From(result.Booking!)),
            BookingCancellationOutcome.NotFound => ApplicationResult<BookingDto>.Failure(new ApplicationError(
                "booking.not-found", "Booking was not found.", ApplicationErrorType.NotFound)),
            BookingCancellationOutcome.Ineligible => ApplicationResult<BookingDto>.Failure(new ApplicationError(
                "booking.cannot-cancel", "Only pending reservations before their expiration can be cancelled.",
                ApplicationErrorType.Conflict)),
            _ => throw new InvalidOperationException("Unknown booking cancellation outcome.")
        };
    }
}
