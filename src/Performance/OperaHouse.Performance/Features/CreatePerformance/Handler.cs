using PerformanceEntity = OperaHouse.Performance.Models.Performance;
using OperaHouse.Performance.Models;
using OperaHouse.Performance.Shared;
using FluentValidation;
using OperaHouse.Performance.Persistence;
using OperaHouse.Contracts.Results;

namespace OperaHouse.Performance.Features.CreatePerformance;

public sealed class Handler(
    IPerformanceRepository repository,
    IValidator<PerformanceDetailsInput> detailsValidator,
    TimeProvider timeProvider)
{
    public async Task<ApplicationResult<PerformanceDto>> HandleAsync(
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

        var currentTime = timeProvider.GetUtcNow();
        var performance = new PerformanceEntity(
            input.Title,
            input.Venue,
            input.StartsAt,
            input.Capacity,
            currentTime);

        await repository.AddAsync(
            performance,
            cancellationToken);

        await repository.SaveChangesAsync(
            cancellationToken);

        return ApplicationResult<PerformanceDto>.Success(
            PerformanceDto.From(performance));
    }
}
