namespace OperaHouse.Notification.Email;

internal sealed class PermanentEmailDeliveryException(Exception cause)
    : Exception(cause.Message, cause)
{
}
