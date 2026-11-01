namespace OperaHouse.Notification.Email;

public interface IEmailSender
{
    Task SendAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken);
}
