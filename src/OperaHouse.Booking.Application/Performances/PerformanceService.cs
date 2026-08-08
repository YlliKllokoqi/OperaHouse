using AutoMapper;

namespace OperaHouse.Booking.Application.Performances;

public sealed class PerformanceService(
    IPerformanceRepository performanceRepository,
    TimeProvider timeProvider,
    IMapper mapper)
    : IPerformanceService
{
    public async Task<IReadOnlyList<PerformanceDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var performances = await performanceRepository.GetPublishedUpcomingAsync(
            timeProvider.GetUtcNow(), cancellationToken);

        return mapper.Map<List<PerformanceDto>>(performances);
    }
}
