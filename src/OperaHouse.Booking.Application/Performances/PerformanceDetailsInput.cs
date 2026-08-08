namespace OperaHouse.Booking.Application.Performances;

public sealed record PerformanceDetailsInput(
    string Title,
    string Venue,
    DateTimeOffset StartsAt,
    int Capacity);
