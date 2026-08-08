namespace OperaHouse.Booking.Application.Common;

public sealed record ApplicationError(
    string Code,
    string Message,
    ApplicationErrorType Type,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);
