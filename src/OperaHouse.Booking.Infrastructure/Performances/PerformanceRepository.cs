using Microsoft.EntityFrameworkCore;
using OperaHouse.Booking.Application.Performances;
using OperaHouse.Booking.Domain.Performances;
using OperaHouse.Booking.Infrastructure.Persistence;

namespace OperaHouse.Booking.Infrastructure.Performances;

public sealed class PerformanceRepository(BookingDbContext dbContext) : IPerformanceRepository
{
    public async Task<IReadOnlyList<Performance>> GetPublishedUpcomingAsync(
        DateTimeOffset currentTime,
        CancellationToken cancellationToken)
    {
        return await dbContext.Performances
            .AsNoTracking()
            .Where(p =>
                p.Status == PerformanceStatus.Published
                && p.StartsAt > currentTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Performance>>
        GetAllForAdministrationAsync(
            CancellationToken cancellationToken)
    {
        return await dbContext.Performances
            .AsNoTracking()
            .OrderByDescending(performance => performance.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Performance?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return await dbContext.Performances
            .SingleOrDefaultAsync(
                performance => performance.Id == id,
                cancellationToken);
    }

    public async Task AddAsync(
        Performance performance,
        CancellationToken cancellationToken)
    {
        await dbContext.Performances.AddAsync(
            performance,
            cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
