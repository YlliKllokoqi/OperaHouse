using OperaHouse.Booking.Models;
using OperaHouse.Booking.Persistence;
using OperaHouse.Contracts.Results;

namespace OperaHouse.Booking.Features.GetBooking;

public sealed class Handler(IBookingRepository repository)
{
    public async Task<ApplicationResult<BookingDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var booking = await repository.GetByIdAsync(id, cancellationToken);
        return booking is null
            ? ApplicationResult<BookingDto>.Failure(new ApplicationError(
                "booking.not-found", "Booking was not found.", ApplicationErrorType.NotFound))
            : ApplicationResult<BookingDto>.Success(BookingDto.From(booking));
    }
}
