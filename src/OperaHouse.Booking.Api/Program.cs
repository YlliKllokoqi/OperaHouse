using AutoMapper;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using OperaHouse.Booking.Api.Security;
using OperaHouse.Booking.Application.Bookings;
using OperaHouse.Booking.Application.Mapping;
using OperaHouse.Booking.Application.Performances;
using OperaHouse.Booking.Application.Validation;
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
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddAutoMapper(
    _ => { },
    typeof(BookingMappingProfile).Assembly);
builder.Services.AddValidatorsFromAssemblyContaining<
    ValidationAssemblyMarker>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<IBookingRepository, BookingRepository>();
builder.Services.AddScoped<IPerformanceService, PerformanceService>();
builder.Services.AddScoped<
    IPerformanceManagementService,
    PerformanceManagementService>();
builder.Services.AddScoped<IPerformanceRepository, PerformanceRepository>();
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
    .AddCheck<RabbitMqHealthCheck>("rabbitmq");

var app = builder.Build();

app.Services
    .GetRequiredService<IMapper>()
    .ConfigurationProvider
    .AssertConfigurationIsValid();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();
