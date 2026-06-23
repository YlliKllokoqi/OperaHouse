using Microsoft.EntityFrameworkCore;
using OperaHouse.Booking.Application.Performances;
using OperaHouse.Booking.Domain.Performances;
using OperaHouse.Booking.Infrastructure.Persistence;

namespace OperaHouse.Booking.Infrastructure.Performances;

public sealed class PerformanceRepository(BookingDbContext dbContext) : IPerformanceRepository
{
    public async Task<IReadOnlyList<Performance>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Performances
            .AsNoTracking()
            .OrderBy(performance => performance.StartsAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Performance?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return await dbContext.Performances
            .AsNoTracking()
            .SingleOrDefaultAsync(
                performance => performance.Id == id,
                cancellationToken);
    }
}
