using System.ComponentModel.DataAnnotations;

namespace OperaHouse.Booking.Api.Models.Bookings;

public sealed class CreateBookingRequest
{
    public Guid PerformanceId { get; init; }

    [Required]
    [EmailAddress]
    [MaxLength(320)]
    public string CustomerEmail { get; init; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int Seats { get; init; }
}
