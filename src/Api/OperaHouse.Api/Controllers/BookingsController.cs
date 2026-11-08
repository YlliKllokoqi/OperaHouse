using System.Net;
using OperaHouse.Api.Models.Bookings;
using Features = OperaHouse.Booking.Features;
using OperaHouse.Api.Errors;
using Microsoft.AspNetCore.Mvc;
using OperaHouse.Booking.Features.CreateBooking;
using OperaHouse.Booking.Models;

namespace OperaHouse.Api.Controllers;

[ApiController]
public sealed class BookingsController : ControllerBase
{
    public const string RouteName = "booking-by-id";

    [HttpGet("bookings/{id:guid}", Name = RouteName)]
    public async Task<ActionResult<BookingDto>> GetById(Guid id,
        [FromServices] Features.GetBooking.Handler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToActionResult(result.Error!);
    }

    [HttpPost("bookings/{id:guid}/cancel")]
    public async Task<ActionResult<BookingDto>> Cancel(Guid id,
        [FromServices] Features.CancelBooking.Handler handler, [FromServices] IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var remoteAddress = HttpContext.Connection.RemoteIpAddress;
        // shortcut: cancellation is local only, replace this guard when guest ownership is enforced.
        if (!environment.IsDevelopment() || remoteAddress is null || !IPAddress.IsLoopback(remoteAddress))
        {
            return NotFound();
        }

        var result = await handler.HandleAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToActionResult(result.Error!);
    }

    [HttpPost("bookings")]
    public async Task<ActionResult<BookingDto>> Create(
        [FromHeader(Name = "Idempotency-Key")] Guid idempotencyKey,
        CreateBookingRequest request,
        [FromServices] Features.CreateBooking.Handler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new CreateBookingInput(idempotencyKey,
            request.PerformanceId, request.CustomerEmail, request.Seats), cancellationToken);
        if (!result.IsSuccess) return this.ToActionResult(result.Error!);
        return CreatedAtRoute(RouteName, new { id = result.Value!.Id }, result.Value);
    }
}
