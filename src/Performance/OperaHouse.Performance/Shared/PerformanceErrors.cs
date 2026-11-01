using OperaHouse.Contracts.Results;
using OperaHouse.Performance.Models;

namespace OperaHouse.Performance.Shared;

internal static class PerformanceErrors
{
    public static ApplicationResult<PerformanceDto> NotFound() =>
        ApplicationResult<PerformanceDto>.Failure(new ApplicationError(
            "performance.not-found", "Performance was not found.", ApplicationErrorType.NotFound));
}
