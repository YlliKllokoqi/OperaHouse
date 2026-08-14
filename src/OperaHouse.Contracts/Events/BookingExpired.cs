namespace OperaHouse.Contracts.Events;

public sealed record BookingExpired(
    Guid MessageId,
    Guid CorrelationId,
    Guid BookingId,
    Guid PerformanceId, 
    int Seats,
    DateTimeOffset OccuredAt);