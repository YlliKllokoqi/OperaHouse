using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OperaHouse.Booking.Api.Errors;
using OperaHouse.Booking.Api.Security;
using OperaHouse.Booking.Application.Performances;

namespace OperaHouse.Booking.Api.Controllers.Admin;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.Admin)]
[Route("admin/performances")]
public sealed class AdminPerformancesController(
    IPerformanceManagementService performanceManagementService)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PerformanceDto>>> GetAll(
        CancellationToken cancellationToken)
    {
        var performances =
            await performanceManagementService.GetAllAsync(
                cancellationToken);

        return Ok(performances);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PerformanceDto>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result =
            await performanceManagementService.GetByIdAsync(
                id,
                cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : this.ToActionResult(result.Error!);
    }

    [HttpPost]
    public async Task<ActionResult<PerformanceDto>> Create(
        PerformanceDetailsInput input,
        CancellationToken cancellationToken)
    {
        var result =
            await performanceManagementService.CreateAsync(
                input,
                cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        var performance = result.Value!;

        return CreatedAtAction(
            nameof(GetById),
            new { id = performance.Id },
            performance);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PerformanceDto>> Update(
        Guid id,
        PerformanceDetailsInput input,
        CancellationToken cancellationToken)
    {
        var result =
            await performanceManagementService.UpdateAsync(
                id,
                input,
                cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : this.ToActionResult(result.Error!);
    }

    [HttpPost("{id:guid}/publish")]
    public async Task<ActionResult<PerformanceDto>> Publish(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result =
            await performanceManagementService.PublishAsync(
                id,
                cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : this.ToActionResult(result.Error!);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<PerformanceDto>> Cancel(
        Guid id,
        CancelPerformanceInput input,
        CancellationToken cancellationToken)
    {
        var result =
            await performanceManagementService.CancelAsync(
                id,
                input,
                cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : this.ToActionResult(result.Error!);
    }
}
