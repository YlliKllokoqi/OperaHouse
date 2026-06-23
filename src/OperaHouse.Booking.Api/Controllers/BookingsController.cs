using Microsoft.AspNetCore.Mvc;
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
        var booking = await bookingService.GetByIdAsync(
            id,
            cancellationToken);

        return booking is null
            ? NotFound()
            : Ok(booking);
    }

    [HttpPost]
    public async Task<ActionResult<BookingDto>> Create(
        CreateBookingRequest request,
        CancellationToken cancellationToken)
    {
        if (request.PerformanceId == Guid.Empty)
        {
            ModelState.AddModelError(
                nameof(request.PerformanceId),
                "PerformanceId is required.");

            return ValidationProblem(ModelState);
        }

        var booking = await bookingService.CreateAsync(
            request.PerformanceId,
            request.CustomerEmail,
            request.Seats,
            cancellationToken);

        if (booking is null)
        {
            return NotFound(new
            {
                Message = "Performance was not found."
            });
        }

        return CreatedAtAction(
            nameof(GetById),
            new { id = booking.Id },
            booking);
    }
}
