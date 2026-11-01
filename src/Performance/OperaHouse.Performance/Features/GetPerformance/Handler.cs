using OperaHouse.Performance.Models;
using OperaHouse.Performance.Shared;
using OperaHouse.Performance.Persistence;
using OperaHouse.Contracts.Results;

namespace OperaHouse.Performance.Features.GetPerformance;

public sealed class Handler(IPerformanceRepository repository)
{
    public async Task<ApplicationResult<PerformanceDto>> HandleAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var performance = await repository.GetByIdAsync(id, cancellationToken);

        if (performance is null)
        {
            return PerformanceErrors.NotFound();
        }

        return ApplicationResult<PerformanceDto>.Success(
            PerformanceDto.From(performance));
    }
}
