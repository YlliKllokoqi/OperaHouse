using Features = OperaHouse.Performance.Features;
using OperaHouse.Api.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OperaHouse.Contracts.Results;
using OperaHouse.Api.Security;
using OperaHouse.Performance.Features.CancelPerformance;
using OperaHouse.Performance.Models;
using OperaHouse.Performance.Shared;

namespace OperaHouse.Api.Controllers;

[ApiController]
public sealed class PerformancesController : ControllerBase
{
    public const string RouteName = "performance-by-id";

    [HttpGet("performances")]
    public async Task<ActionResult<IReadOnlyList<PerformanceDto>>> Browse(
        [FromServices] Features.BrowsePerformances.Handler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(cancellationToken);
        return Ok(result);
    }

    //[Authorize(Policy = AuthorizationPolicies.Admin)]
    [HttpGet("admin/performances")]
    public async Task<ActionResult<IReadOnlyList<PerformanceDto>>> GetAll(
        [FromServices] Features.ListPerformances.Handler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(cancellationToken);
        return Ok(result);
    }

    //[Authorize(Policy = AuthorizationPolicies.Admin)]
    [HttpGet("admin/performances/{id:guid}", Name = RouteName)]
    public async Task<ActionResult<PerformanceDto>> GetById(Guid id,
        [FromServices] Features.GetPerformance.Handler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToActionResult(result.Error!);
    }

    //[Authorize(Policy = AuthorizationPolicies.Admin)]
    [HttpPost("admin/performances")]
    public async Task<ActionResult<PerformanceDto>> Create(PerformanceDetailsInput input,
        [FromServices] Features.CreatePerformance.Handler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(input, cancellationToken);
        if (!result.IsSuccess) return this.ToActionResult(result.Error!);
        return CreatedAtRoute(RouteName, new { id = result.Value!.Id }, result.Value);
    }

    //[Authorize(Policy = AuthorizationPolicies.Admin)]
    [HttpPut("admin/performances/{id:guid}")]
    public async Task<ActionResult<PerformanceDto>> Update(Guid id, PerformanceDetailsInput input,
        [FromServices] Features.UpdatePerformance.Handler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, input, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToActionResult(result.Error!);
    }

    //[Authorize(Policy = AuthorizationPolicies.Admin)]
    [HttpPost("admin/performances/{id:guid}/publish")]
    public async Task<ActionResult<PerformanceDto>> Publish(Guid id,
        [FromServices] Features.PublishPerformance.Handler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToActionResult(result.Error!);
    }

    //[Authorize(Policy = AuthorizationPolicies.Admin)]
    [HttpPost("admin/performances/{id:guid}/cancel")]
    public async Task<ActionResult<PerformanceDto>> Cancel(Guid id, CancelPerformanceInput input,
        [FromServices] Features.CancelPerformance.Handler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, input, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToActionResult(result.Error!);
    }
}
