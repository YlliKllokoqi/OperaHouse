using OperaHouse.Performance.Models;
using OperaHouse.Performance.Shared;
using FluentValidation;
using OperaHouse.Performance.Persistence;
using OperaHouse.Contracts.Results;

namespace OperaHouse.Performance.Features.CancelPerformance;

public sealed class Handler(
    IPerformanceRepository repository,
    IValidator<CancelPerformanceInput> cancellationValidator,
    TimeProvider timeProvider)
{
    public async Task<ApplicationResult<PerformanceDto>> HandleAsync(
        Guid id,
        CancelPerformanceInput input,
        CancellationToken cancellationToken)
    {
        var validationResult = await cancellationValidator.ValidateAsync(
            input,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return ApplicationResult<PerformanceDto>.Failure(
                ApplicationError.FromValidation("performance.cancellation-validation",
                    "The cancellation request is invalid.",
                    validationResult.Errors.Select(error => (error.PropertyName, error.ErrorMessage))));
        }

        var performance = await repository.GetByIdAsync(id, cancellationToken);

        if (performance is null)
        {
            return PerformanceErrors.NotFound();
        }

        if (!performance.Cancel(
                input.Reason,
                timeProvider.GetUtcNow()))
        {
            return ApplicationResult<PerformanceDto>.Failure(
                new ApplicationError(
                    "performance.not-cancellable",
                    "A cancelled or completed performance cannot be cancelled.",
                    ApplicationErrorType.Conflict));
        }

        await repository.SaveChangesAsync(
            cancellationToken);

        return ApplicationResult<PerformanceDto>.Success(
            PerformanceDto.From(performance));
    }
}
