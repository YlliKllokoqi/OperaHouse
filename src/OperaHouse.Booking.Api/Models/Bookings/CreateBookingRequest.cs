namespace OperaHouse.Booking.Api.Models.Bookings;

public sealed class CreateBookingRequest
{
    public Guid PerformanceId { get; init; }

    public string CustomerEmail { get; init; } = string.Empty;

    public int Seats { get; init; }
}
