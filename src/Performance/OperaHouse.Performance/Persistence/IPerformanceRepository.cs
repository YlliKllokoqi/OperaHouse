using PerformanceEntity = OperaHouse.Performance.Models.Performance;

namespace OperaHouse.Performance.Persistence;

public interface IPerformanceRepository
{
    Task<IReadOnlyList<PerformanceEntity>> GetPublishedUpcomingAsync(DateTimeOffset currentTime,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<PerformanceEntity>> GetAllForAdministrationAsync(CancellationToken cancellationToken);
    Task<PerformanceEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(PerformanceEntity performance, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
