using OperaHouse.Booking.Domain.Performances;

namespace OperaHouse.Booking.Application.Performances;

public interface IPerformanceRepository
{
    Task<Performance?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Performance>> GetPublishedUpcomingAsync(DateTimeOffset currentTime,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Performance>> GetAllForAdministrationAsync(CancellationToken cancellationToken);

    Task AddAsync(Performance performance,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
