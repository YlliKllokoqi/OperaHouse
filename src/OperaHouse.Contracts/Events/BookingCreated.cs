namespace OperaHouse.Contracts.Events;

public record BookingCreated(
    Guid MessageId,
    Guid CorrelationId,
    Guid BookingId,
    Guid PerformanceId,
    string CustomerEmail,
    int Seats,
    DateTimeOffset OccuredAt);