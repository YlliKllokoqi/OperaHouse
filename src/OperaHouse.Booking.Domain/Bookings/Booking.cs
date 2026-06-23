namespace OperaHouse.Booking.Domain.Bookings;

public class Booking
{
    public Guid Id { get; set; }
    public Guid PerformanceId { get; set; }
    public string CustomerEmail { get; set; } = String.Empty;
    public int Seats { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; }
}