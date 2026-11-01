using OperaHouse.Performance.Models;
using OperaHouse.Performance.Persistence;

namespace OperaHouse.Performance.Features.ListPerformances;

public sealed class Handler(IPerformanceRepository repository)
{
    public async Task<IReadOnlyList<PerformanceDto>> HandleAsync(
        CancellationToken cancellationToken)
    {
        var performances = await repository.GetAllForAdministrationAsync(cancellationToken);

        return performances.Select(PerformanceDto.From).ToList();
    }
}
