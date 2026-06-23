using OperaHouse.Booking.Domain.Performances;

namespace OperaHouse.Booking.Application.Performances;

public interface IPerformanceRepository
{
    Task<IReadOnlyList<Performance>> GetAllAsync(
        CancellationToken cancellationToken);

    Task<Performance?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken);
}
