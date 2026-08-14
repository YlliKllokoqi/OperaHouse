using BookingEntity =
    OperaHouse.Booking.Domain.Bookings.Booking;

namespace OperaHouse.Booking.Application.Bookings;

public sealed record BookingCreationPersistenceResult(BookingCreationOutcome Outcome,
    BookingEntity? Booking = null);