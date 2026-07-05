using Microsoft.EntityFrameworkCore;
using OperaHouse.Messaging;
using OperaHouse.Notification.Application.Inbox;
using OperaHouse.Notification.Application.Notifications;
using OperaHouse.Notification.Infrastructure.Inbox;
using OperaHouse.Notification.Infrastructure.Notifications;
using OperaHouse.Notification.Infrastructure.Persistence;
using OperaHouse.Notification.Worker;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("NotificationDatabase")
    ?? throw new InvalidOperationException("Connection string 'NotificationDatabase' was not found.");

builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection("RabbitMq"));

builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IInboxRepository, InboxRepository>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<INotificationUnitOfWork, NotificationUnitOfWork>();
builder.Services.AddScoped<IEmailSender, FakeEmailSender>();
builder.Services.AddScoped<INotificationProcessor, NotificationProcessor>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
