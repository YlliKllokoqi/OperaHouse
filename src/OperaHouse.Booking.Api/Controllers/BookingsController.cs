using Microsoft.AspNetCore.Mvc;
using OperaHouse.Booking.Api.Errors;
using OperaHouse.Booking.Api.Models.Bookings;
using OperaHouse.Booking.Application.Bookings;

namespace OperaHouse.Booking.Api.Controllers;

[ApiController]
[Route("bookings")]
public sealed class BookingsController(IBookingService bookingService) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BookingDto>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await bookingService.GetByIdAsync(
            id,
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : this.ToActionResult(result.Error!);
    }

    [HttpPost]
    public async Task<ActionResult<BookingDto>> Create(
        CreateBookingRequest request,
        CancellationToken cancellationToken)
    {
        var input = new CreateBookingInput(
            request.PerformanceId,
            request.CustomerEmail,
            request.Seats);

        var result = await bookingService.CreateAsync(
            input,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error!);
        }

        var booking = result.Value!;

        return CreatedAtAction(
            nameof(GetById),
            new { id = booking.Id },
            booking);
    }
}
