using BookingEntity = OperaHouse.Booking.Models.Booking;

namespace OperaHouse.Booking.Persistence;

public enum BookingCancellationOutcome
{
    Cancelled,
    NotFound,
    Ineligible
}

public sealed record BookingCancellationPersistenceResult(
    BookingCancellationOutcome Outcome, BookingEntity? Booking = null);
