namespace OperaHouse.Contracts.Events;

public sealed record BookingCancelled(
    Guid MessageId,
    Guid CorrelationId,
    Guid BookingId,
    Guid PerformanceId,
    string CustomerEmail,
    int Seats,
    DateTimeOffset OccurredAt);
