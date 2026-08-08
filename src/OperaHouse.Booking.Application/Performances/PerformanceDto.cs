using OperaHouse.Booking.Domain.Performances;

namespace OperaHouse.Booking.Application.Performances;

public record PerformanceDto(
    Guid Id,
    string Title,
    string Venue,
    DateTimeOffset StartsAt,
    int Capacity,
    int AvailableSeats,
    PerformanceStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? CancelledAt,
    string? CancellationReason);
