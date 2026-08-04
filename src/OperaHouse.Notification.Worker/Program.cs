using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MimeKit;
using OperaHouse.Messaging;
using OperaHouse.Notification.Application.Inbox;
using OperaHouse.Notification.Application.Notifications;
using OperaHouse.Notification.Infrastructure.Email;
using OperaHouse.Notification.Infrastructure.Inbox;
using OperaHouse.Notification.Infrastructure.Notifications;
using OperaHouse.Notification.Infrastructure.HealthChecks;
using OperaHouse.Notification.Infrastructure.Persistence;
using OperaHouse.Notification.Worker;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("NotificationDatabase")
    ?? throw new InvalidOperationException("Connection string 'NotificationDatabase' was not found.");

builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection("RabbitMq"));

builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddOptions<EmailOptions>()
    .Bind(builder.Configuration.GetSection(EmailOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.Host),
        "Email: Host is required.")
    .Validate(
        options => options.Port is > 0 and <= 65535,
        "Email: Port must be a valid TCP port."
    )
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.FromAddress),
        "Email: FromAdress is required."
    )
    .Validate(
        options => MailboxAddress.TryParse(
            options.FromAddress,
            out _),
        "Email: FromAddress must be a valid email address."
    )
    .ValidateOnStart();

builder.Services.AddScoped<IInboxRepository, InboxRepository>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<INotificationUnitOfWork, NotificationUnitOfWork>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<INotificationProcessor, NotificationProcessor>();
builder.Services.AddHealthChecks()
    .AddCheck<NotificationDatabaseHealthCheck>("notification-database")
    .AddCheck<RabbitMqHealthCheck>("rabbitmq");
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<HealthLoggingWorker>();

var host = builder.Build();
host.Run();
