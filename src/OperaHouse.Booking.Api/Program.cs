using Microsoft.EntityFrameworkCore;
using OperaHouse.Booking.Application.Bookings;
using OperaHouse.Booking.Application.Performances;
using OperaHouse.Booking.Infrastructure.Bookings;
using OperaHouse.Booking.Infrastructure.Outbox;
using OperaHouse.Booking.Infrastructure.Performances;
using OperaHouse.Booking.Infrastructure.Persistence;
using OperaHouse.Messaging;
using OperaHouse.Notification.Infrastructure.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("BookingDatabase");

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<BookingDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddScoped<RabbitMqPublisher>();
builder.Services.AddHostedService<OutboxPublisherWorker>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<IBookingRepository, BookingRepository>();
builder.Services.AddScoped<IPerformanceService, PerformanceService>();
builder.Services.AddScoped<IPerformanceRepository, PerformanceRepository>();
builder.Services.AddHealthChecks()
    .AddCheck<BookingDatabaseHealthCheck>("booking-database")
    .AddCheck<RabbitMqHealthCheck>("rabbitmq");

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/health");
app.MapControllers();
app.UseHttpsRedirection();

app.Run();
