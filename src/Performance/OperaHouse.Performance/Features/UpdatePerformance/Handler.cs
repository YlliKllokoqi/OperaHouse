using OperaHouse.Performance.Models;
using OperaHouse.Performance.Shared;
using FluentValidation;
using OperaHouse.Performance.Persistence;
using OperaHouse.Contracts.Results;

namespace OperaHouse.Performance.Features.UpdatePerformance;

public sealed class Handler(
    IPerformanceRepository repository,
    IValidator<PerformanceDetailsInput> detailsValidator,
    TimeProvider timeProvider)
{
    public async Task<ApplicationResult<PerformanceDto>> HandleAsync(
        Guid id,
        PerformanceDetailsInput input,
        CancellationToken cancellationToken)
    {
        var validationResult = await detailsValidator.ValidateAsync(
            input,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return ApplicationResult<PerformanceDto>.Failure(
                ApplicationError.FromValidation("performance.validation",
                    "The performance request is invalid.",
                    validationResult.Errors.Select(error => (error.PropertyName, error.ErrorMessage))));
        }

        var performance = await repository.GetByIdAsync(id, cancellationToken);

        if (performance is null)
        {
            return PerformanceErrors.NotFound();
        }

        if (!performance.UpdateDraft(
                input.Title,
                input.Venue,
                input.StartsAt,
                input.Capacity,
                timeProvider.GetUtcNow()))
        {
            return ApplicationResult<PerformanceDto>.Failure(
                new ApplicationError(
                    "performance.not-editable",
                    "Only draft performances can be edited.",
                    ApplicationErrorType.Conflict));
        }

        await repository.SaveChangesAsync(
            cancellationToken);

        return ApplicationResult<PerformanceDto>.Success(
            PerformanceDto.From(performance));
    }
}
