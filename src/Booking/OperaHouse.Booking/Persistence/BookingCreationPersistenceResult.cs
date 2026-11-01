using BookingEntity =
    OperaHouse.Booking.Models.Booking;

namespace OperaHouse.Booking.Persistence;

public sealed record BookingCreationPersistenceResult(BookingCreationOutcome Outcome,
    BookingEntity? Booking = null);
