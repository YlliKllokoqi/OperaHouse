using OperaHouse.Performance.Models;
using OperaHouse.Performance.Persistence;

namespace OperaHouse.Performance.Features.BrowsePerformances;

public sealed class Handler(IPerformanceRepository repository, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<PerformanceDto>> HandleAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var performances = await repository.GetPublishedUpcomingAsync(now, cancellationToken);
        return performances.Select(PerformanceDto.From).ToList();
    }
}
