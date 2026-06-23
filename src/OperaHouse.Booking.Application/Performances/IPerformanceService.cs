namespace OperaHouse.Booking.Application.Performances;

public interface IPerformanceService
{
    Task<IReadOnlyList<PerformanceDto>> GetAllAsync(CancellationToken cancellationToken);
}