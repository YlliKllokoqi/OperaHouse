using PerformanceEntity = OperaHouse.Performance.Models.Performance;

namespace OperaHouse.Performance.Models;

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
    string? CancellationReason)
{
    public static PerformanceDto From(PerformanceEntity performance) => new(
        performance.Id, performance.Title, performance.Venue, performance.StartsAt,
        performance.Capacity, performance.AvailableSeats, performance.Status,
        performance.CreatedAt, performance.UpdatedAt, performance.PublishedAt,
        performance.CancelledAt, performance.CancellationReason);
}
