using AutoMapper;
using FluentValidation;
using OperaHouse.Booking.Application.Common;
using OperaHouse.Booking.Domain.Performances;

namespace OperaHouse.Booking.Application.Performances;

public sealed class PerformanceManagementService(
    IPerformanceRepository performanceRepository,
    IValidator<PerformanceDetailsInput> detailsValidator,
    IValidator<CancelPerformanceInput> cancellationValidator,
    TimeProvider timeProvider,
    IMapper mapper)
    : IPerformanceManagementService
{
    public async Task<IReadOnlyList<PerformanceDto>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        var performances = await performanceRepository
            .GetAllForAdministrationAsync(cancellationToken);

        return mapper.Map<List<PerformanceDto>>(performances);
    }

    public async Task<ApplicationResult<PerformanceDto>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var performance = await performanceRepository.GetByIdAsync(
            id,
            cancellationToken);

        if (performance is null)
        {
            return NotFound();
        }

        return ApplicationResult<PerformanceDto>.Success(
            mapper.Map<PerformanceDto>(performance));
    }

    public async Task<ApplicationResult<PerformanceDto>> CreateAsync(
        PerformanceDetailsInput input,
        CancellationToken cancellationToken)
    {
        var validationResult = await detailsValidator.ValidateAsync(
            input,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return ApplicationResult<PerformanceDto>.Failure(
                validationResult.ToApplicationError(
                    "performance.validation",
                    "The performance request is invalid."));
        }

        var currentTime = timeProvider.GetUtcNow();
        var performance = new Performance(
            input.Title,
            input.Venue,
            input.StartsAt,
            input.Capacity,
            currentTime);

        await performanceRepository.AddAsync(
            performance,
            cancellationToken);

        await performanceRepository.SaveChangesAsync(
            cancellationToken);

        return ApplicationResult<PerformanceDto>.Success(
            mapper.Map<PerformanceDto>(performance));
    }

    public async Task<ApplicationResult<PerformanceDto>> UpdateAsync(
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
                validationResult.ToApplicationError(
                    "performance.validation",
                    "The performance request is invalid."));
        }

        var performance = await performanceRepository.GetByIdAsync(
            id,
            cancellationToken);

        if (performance is null)
        {
            return NotFound();
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

        await performanceRepository.SaveChangesAsync(
            cancellationToken);

        return ApplicationResult<PerformanceDto>.Success(
            mapper.Map<PerformanceDto>(performance));
    }

    public async Task<ApplicationResult<PerformanceDto>> PublishAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var performance = await performanceRepository.GetByIdAsync(
            id,
            cancellationToken);

        if (performance is null)
        {
            return NotFound();
        }

        if (!performance.Publish(timeProvider.GetUtcNow()))
        {
            return ApplicationResult<PerformanceDto>.Failure(
                new ApplicationError(
                    "performance.not-publishable",
                    "Only a future draft performance can be published.",
                    ApplicationErrorType.Conflict));
        }

        await performanceRepository.SaveChangesAsync(
            cancellationToken);

        return ApplicationResult<PerformanceDto>.Success(
            mapper.Map<PerformanceDto>(performance));
    }

    public async Task<ApplicationResult<PerformanceDto>> CancelAsync(
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
                validationResult.ToApplicationError(
                    "performance.cancellation-validation",
                    "The cancellation request is invalid."));
        }

        var performance = await performanceRepository.GetByIdAsync(
            id,
            cancellationToken);

        if (performance is null)
        {
            return NotFound();
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

        await performanceRepository.SaveChangesAsync(
            cancellationToken);

        return ApplicationResult<PerformanceDto>.Success(
            mapper.Map<PerformanceDto>(performance));
    }

    private static ApplicationResult<PerformanceDto> NotFound()
    {
        return ApplicationResult<PerformanceDto>.Failure(
            new ApplicationError(
                "performance.not-found",
                "Performance was not found.",
                ApplicationErrorType.NotFound));
    }
}
