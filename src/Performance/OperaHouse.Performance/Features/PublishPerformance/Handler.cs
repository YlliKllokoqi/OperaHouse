using OperaHouse.Performance.Models;
using OperaHouse.Performance.Shared;
using OperaHouse.Performance.Persistence;
using OperaHouse.Contracts.Results;

namespace OperaHouse.Performance.Features.PublishPerformance;

public sealed class Handler(IPerformanceRepository repository, TimeProvider timeProvider)
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

        if (!performance.Publish(timeProvider.GetUtcNow()))
        {
            return ApplicationResult<PerformanceDto>.Failure(
                new ApplicationError(
                    "performance.not-publishable",
                    "Only a future draft performance can be published.",
                    ApplicationErrorType.Conflict));
        }

        await repository.SaveChangesAsync(
            cancellationToken);

        return ApplicationResult<PerformanceDto>.Success(
            PerformanceDto.From(performance));
    }
}
