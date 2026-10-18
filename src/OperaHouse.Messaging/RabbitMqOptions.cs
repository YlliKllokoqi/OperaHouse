namespace OperaHouse.Messaging;

public class RabbitMqOptions
{
    public string HostName { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 5672;
    public string UserName { get; init; } = "guest";
    public string Password { get; init; } = "guest";
    public string ExchangeName { get; init; } = "operahouse.events";
    public string BookingCreatedRoutingKey { get; init; } = "booking.created";
    public string BookingCreatedQueueName { get; init; } = "notification-booking-created.queue";
    public string DeadLetterExchangeName { get; init; } = "operahouse.dead-letter";
    public string BookingCreatedDeadLetterQueueName { get; init; } = "notification-booking-created.dead-letter.queue";
    public string BookingCreatedDeadLetterRoutingKey { get; init; } = "booking.created.dead";
    public string RetryExchangeName { get; init; } = "operahouse.retry";
    public string BookingCreatedRetryQueueName { get; init; } = "notification-booking-created.retry.queue";
    public string BookingCreatedRetryRoutingKey { get; init; } = "booking.created.retry";
    public int BookingCreatedRetryDelayMilliseconds { get; init; } = 10000;
    public int BookingCreatedMaxRetryAttempts { get; init; } = 3;
    public string BookingExpiredRoutingKey { get; init; } = "booking.expired";
    public string BookingExpiredQueueName { get; init; } = "notification-booking-expired.queue";

    public string BookingExpiredRetryQueueName { get; init; } = "notification-booking-expired.retry.queue";

    public string BookingExpiredRetryRoutingKey { get; init; } = "booking.expired.retry";

    public int BookingExpiredRetryDelayMilliseconds { get; init; } = 10_000;

    public int BookingExpiredMaxRetryAttempts { get; init; } = 3;

    public string BookingExpiredDeadLetterQueueName { get; init; } = "notification-booking-expired.dead-letter.queue";

    public string BookingExpiredDeadLetterRoutingKey { get; init; } = "booking.expired.dead";
}