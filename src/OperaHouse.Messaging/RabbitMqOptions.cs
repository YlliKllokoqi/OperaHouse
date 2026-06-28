namespace OperaHouse.Messaging;

public class RabbitMqOptions
{
    public string HostName { get; init; } = "localhost";
    public int Port { get; init; } = 5672;
    public string UserName { get; init; } = "guest";
    public string Password { get; init; } = "guest";
    public string ExchangeName { get; init; } = "operahouse.events";
    public string BookingCreatedRoutingKey { get; init; } = "booking.created";
    public string BookingCreatedQueueName { get; init; } = "notification-booking-created.queue";
}