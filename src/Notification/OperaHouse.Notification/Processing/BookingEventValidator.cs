using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using OperaHouse.Contracts.Events;

namespace OperaHouse.Notification.Processing;

internal static class BookingEventValidator
{
    public static void Validate(BookingCreated message)
    {
        Validate(message.MessageId, message.CorrelationId, message.BookingId,
            message.PerformanceId, message.CustomerEmail, message.Seats, message.OccurredAt);

        if (message.ExpiresAt <= message.OccurredAt)
        {
            throw new ValidationException("Booking expiration must be after creation.");
        }
    }

    public static void Validate(BookingExpired message) =>
        Validate(message.MessageId, message.CorrelationId, message.BookingId,
            message.PerformanceId, message.CustomerEmail, message.Seats, message.OccurredAt);

    private static void Validate(Guid messageId, Guid correlationId, Guid bookingId,
        Guid performanceId, string? customerEmail, int seats, DateTimeOffset occurredAt)
    {
        if (messageId == Guid.Empty || correlationId == Guid.Empty
            || bookingId == Guid.Empty || performanceId == Guid.Empty)
        {
            throw new ValidationException("Booking event identifiers must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(customerEmail) || customerEmail.Length > 320
            || !MailAddress.TryCreate(customerEmail, out var address)
            || address.Address != customerEmail)
        {
            throw new ValidationException("Booking event requires a valid customer email.");
        }

        if (seats <= 0 || occurredAt == default)
        {
            throw new ValidationException("Booking event requires positive seats and an occurrence time.");
        }
    }
}
