using OperaHouse.Messaging;
using OperaHouse.Notification.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
