using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace OperaHouse.Notification.Email;

public class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public async Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
    {
        var message = new MimeMessage();

        message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;

        message.Body = new TextPart("plain")
        {
            Text = body
        };

        using var client = new SmtpClient();

        var security = _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;

        try
        {
            await client.ConnectAsync(_options.Host, _options.Port, security, cancellationToken);

            if (!string.IsNullOrWhiteSpace(_options.UserName))
            {
                await client.AuthenticateAsync(_options.UserName, _options.Password, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
        }
        catch (Exception exception) when (IsPermanentRejection(exception))
        {
            throw new PermanentEmailDeliveryException(exception);
        }

        try
        {
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
        catch (Exception exception)
        {
            // A failed QUIT must not turn an accepted email into a failed send.
            logger.LogDebug(exception, "SMTP disconnect failed after email acceptance.");
        }
    }

    internal static bool IsPermanentRejection(Exception exception)
    {
        if (exception is ServiceNotAuthenticatedException)
        {
            return true;
        }

        var reply = exception switch
        {
            SmtpCommandException command => command,
            AuthenticationException { InnerException: SmtpCommandException command } => command,
            _ => null
        };

        return reply is not null && (int)reply.StatusCode is >= 500 and < 600;
    }
}
