using Microsoft.EntityFrameworkCore;
using OperaHouse.Performance.Models;
using OperaHouse.Performance.Persistence;
using PerformanceEntity = OperaHouse.Performance.Models.Performance;

namespace OperaHouse.Persistence.Performances;

public sealed class PerformanceRepository(BookingDbContext dbContext) : IPerformanceRepository
{
    public async Task<IReadOnlyList<PerformanceEntity>> GetPublishedUpcomingAsync(DateTimeOffset currentTime,
        CancellationToken cancellationToken) =>
        await dbContext.Performances.AsNoTracking()
            .Where(performance => performance.Status == PerformanceStatus.Published && performance.StartsAt > currentTime)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PerformanceEntity>> GetAllForAdministrationAsync(CancellationToken cancellationToken) =>
        await dbContext.Performances.AsNoTracking().OrderByDescending(performance => performance.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<PerformanceEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Performances.SingleOrDefaultAsync(performance => performance.Id == id, cancellationToken);

    public async Task AddAsync(PerformanceEntity performance, CancellationToken cancellationToken) =>
        await dbContext.Performances.AddAsync(performance, cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await dbContext.SaveChangesAsync(cancellationToken);
}
