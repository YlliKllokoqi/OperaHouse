using OperaHouse.Booking.Application.Common;

namespace OperaHouse.Booking.Application.Performances;

public interface IPerformanceManagementService
{
    Task<IReadOnlyList<PerformanceDto>> GetAllAsync(CancellationToken cancellationToken);
    Task<ApplicationResult<PerformanceDto>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<ApplicationResult<PerformanceDto>> CreateAsync(
        PerformanceDetailsInput input,
        CancellationToken cancellationToken);

    Task<ApplicationResult<PerformanceDto>> UpdateAsync(
        Guid id,
        PerformanceDetailsInput input,
        CancellationToken cancellationToken);

    Task<ApplicationResult<PerformanceDto>> PublishAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<ApplicationResult<PerformanceDto>> CancelAsync(
        Guid id,
        CancelPerformanceInput input,
        CancellationToken cancellationToken);
}
