namespace OperaHouse.Booking.Application.Performances;

public record PerformanceDto(
    Guid Id,
    string Title,
    string Venue,
    DateTimeOffset StartsAt,
    int AvailableSeats);