namespace OperaHouse.Booking.Domain.Performances;

public class Performance
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Venue { get; set; } = String.Empty;
    public DateTimeOffset StartsAt { get; set; }
    public int AvailableSeats { get; set; }
}