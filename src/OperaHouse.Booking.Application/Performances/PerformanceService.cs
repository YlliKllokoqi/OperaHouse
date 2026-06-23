namespace OperaHouse.Booking.Application.Performances;

public sealed class PerformanceService(IPerformanceRepository performanceRepository) : IPerformanceService
{
    public async Task<IReadOnlyList<PerformanceDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var performances = await performanceRepository.GetAllAsync(cancellationToken);

        return performances
            .Select(performance => new PerformanceDto(
                performance.Id,
                performance.Title,
                performance.Venue,
                performance.StartsAt,
                performance.AvailableSeats))
            .ToList();
    }
}