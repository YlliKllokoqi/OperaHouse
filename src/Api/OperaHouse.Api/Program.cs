using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using MimeKit;
using OperaHouse.Api.Security;
using OperaHouse.Booking.Configuration;
using OperaHouse.Booking.Persistence;
using OperaHouse.Messaging;
using OperaHouse.Notification.Email;
using OperaHouse.Notification.Features;
using OperaHouse.Notification.HealthChecks;
using OperaHouse.Notification.Messaging;
using OperaHouse.Notification.Persistence;
using OperaHouse.Notification.Processing;
using OperaHouse.Performance.Persistence;
using OperaHouse.Performance.Shared;
using OperaHouse.Persistence.Bookings;
using OperaHouse.Persistence.HealthChecks;
using OperaHouse.Persistence.Outbox;
using OperaHouse.Persistence.Performances;
using OperaHouse.Persistence;
using RabbitMQ.Client;
using Bookings = OperaHouse.Booking.Features;
using Performances = OperaHouse.Performance.Features;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
{
    // Keep existing notification secrets after consolidating the two hosts.
    builder.Configuration.AddUserSecrets<EmailOptions>(optional: true);
    builder.Configuration.AddUserSecrets<OperaHouse.Api.Controllers.BookingsController>(optional: true);
    builder.Configuration.AddEnvironmentVariables();
    builder.Configuration.AddCommandLine(args);
}

var connectionString = builder.Configuration.GetConnectionString("BookingDatabase");

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOptions<BookingOptions>()
    .Bind(
        builder.Configuration.GetSection(BookingOptions.SectionName))
    .Validate(
        options => options.ReservationDurationMinutes > 0,
        "Booking reservation duration must be greater than zero.")
    .Validate(
        options => options.ExpirationCheckIntervalSeconds > 0,
        "Booking expiration interval must be greater than zero.")
    .Validate(
        options => options.ExpirationBatchSize > 0,
        "Booking expiration batch size must be greater than zero.")
    .ValidateOnStart();

builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition(
        "bearer",
        new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Enter the JWT access token."
        });

    options.AddSecurityRequirement(document =>
        new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(
                "bearer",
                document)] = []
        });
});
builder.Services.AddDbContext<BookingDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddScoped<RabbitMqPublisher>();
builder.Services.AddHostedService<OutboxPublisherWorker>();
builder.Services.AddHostedService<Bookings.ExpireBookings.BookingExpirationWorker>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddValidatorsFromAssemblyContaining<Bookings.CreateBooking.CreateBookingInputValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<PerformanceDetailsInputValidator>();
builder.Services.AddScoped<Bookings.CreateBooking.Handler>();
builder.Services.AddScoped<Bookings.GetBooking.Handler>();
builder.Services.AddScoped<Bookings.ExpireBookings.Handler>();
builder.Services.AddScoped<IBookingRepository, BookingRepository>();
builder.Services.AddScoped<IPerformanceRepository, PerformanceRepository>();
builder.Services.AddScoped<Performances.BrowsePerformances.Handler>();
builder.Services.AddScoped<Performances.ListPerformances.Handler>();
builder.Services.AddScoped<Performances.GetPerformance.Handler>();
builder.Services.AddScoped<Performances.CreatePerformance.Handler>();
builder.Services.AddScoped<Performances.UpdatePerformance.Handler>();
builder.Services.AddScoped<Performances.PublishPerformance.Handler>();
builder.Services.AddScoped<Performances.CancelPerformance.Handler>();
builder.Services.AddSingleton<IConnectionFactory>(services =>
{
    var options = services.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
    return new ConnectionFactory
    {
        HostName = options.HostName,
        Port = options.Port,
        UserName = options.UserName,
        Password = options.Password,
        ConsumerDispatchConcurrency = 1,
        AutomaticRecoveryEnabled = false
    };
});

builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("NotificationDatabase")
        ?? throw new InvalidOperationException("Connection string 'NotificationDatabase' was not found.")));

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
        "Email: FromAddress is required."
    )
    .Validate(
        options => MailboxAddress.TryParse(
            options.FromAddress,
            out _),
        "Email: FromAddress must be a valid email address."
    )
    .ValidateOnStart();

builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<INotificationStore, NotificationStore>();
builder.Services.AddScoped<NotificationProcessor>();
builder.Services.AddScoped<SendBookingConfirmation>();
builder.Services.AddScoped<SendBookingExpiration>();
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<HealthLoggingWorker>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services
    .AddAuthorizationBuilder()
    .AddPolicy(
        AuthorizationPolicies.Admin,
        policy => policy.RequireRole("Admin"));
builder.Services.AddHealthChecks()
    .AddCheck<BookingDatabaseHealthCheck>("booking-database")
    .AddCheck<NotificationDatabaseHealthCheck>("notification-database")
    .AddCheck<RabbitMqHealthCheck>("rabbitmq");

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
}
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();
