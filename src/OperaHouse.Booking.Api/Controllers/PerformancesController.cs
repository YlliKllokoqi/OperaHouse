using Microsoft.AspNetCore.Mvc;
using OperaHouse.Booking.Application.Performances;

namespace OperaHouse.Booking.Api.Controllers;

[ApiController]
[Route(("performances"))]
public class PerformancesController(IPerformanceService performanceService)
            : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PerformanceDto>>> GetAll(CancellationToken cancellationToken)
    {
        var performances = await performanceService.GetAllAsync(cancellationToken);

        return Ok(performances);
    }
}