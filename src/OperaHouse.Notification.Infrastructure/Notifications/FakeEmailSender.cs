using Microsoft.Extensions.Logging;
using OperaHouse.Notification.Application.Notifications;

namespace OperaHouse.Notification.Infrastructure.Notifications;

public sealed class FakeEmailSender(ILogger<FakeEmailSender> logger)
    : IEmailSender
{
    public Task SendAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Email sent to {Recipient}: {Subject}. Body: {Body}",
            recipient,
            subject,
            body);

        return Task.CompletedTask;
    }
}
