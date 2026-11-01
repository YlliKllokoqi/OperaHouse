using PerformanceEntity = OperaHouse.Performance.Models.Performance;
using System.Reflection;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OperaHouse.Api.Controllers;
using OperaHouse.Api.Models.Bookings;
using OperaHouse.Booking.Configuration;
using OperaHouse.Persistence.Bookings;
using OperaHouse.Persistence.Performances;
using OperaHouse.Booking.Features.CreateBooking;
using OperaHouse.Booking.Models;
using OperaHouse.Booking.Persistence;
using OperaHouse.Performance.Features.CancelPerformance;
using OperaHouse.Performance.Features;
using OperaHouse.Performance.Models;
using OperaHouse.Performance.Shared;
using OperaHouse.Persistence;
using OperaHouse.Messaging;
using Bookings = OperaHouse.Booking.Features;
using Performances = OperaHouse.Performance.Features;

namespace OperaHouse.RegressionTests;

internal static class FeatureTests
{
    public static async Task RunAsync()
    {
        var clock = new FixedClock();
        using var services = new ServiceCollection().AddSingleton<TimeProvider>(clock)
            .AddValidatorsFromAssemblyContaining<CreateBookingInputValidator>()
            .AddValidatorsFromAssemblyContaining<PerformanceDetailsInputValidator>().BuildServiceProvider();
        var bookingValidator = services.GetRequiredService<IValidator<CreateBookingInput>>();
        var detailsValidator = services.GetRequiredService<IValidator<PerformanceDetailsInput>>();
        var cancelValidator = services.GetRequiredService<IValidator<CancelPerformanceInput>>();
        Check.That(bookingValidator is CreateBookingInputValidator
            && detailsValidator is PerformanceDetailsInputValidator
            && cancelValidator is CancelPerformanceInputValidator,
            "Co-located feature validators remain discoverable by startup registration");

        // Invalid inputs must return before touching the database.
        await using var database = new BookingDbContext(new DbContextOptionsBuilder<BookingDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused;Username=unused;Password=unused").Options);
        var createBooking = new Bookings.CreateBooking.Handler(new BookingRepository(database, Options.Create(new RabbitMqOptions())),
            bookingValidator, Options.Create(new BookingOptions()), clock);
        var bookingResponse = await new BookingsController().Create(Guid.Empty,
            new CreateBookingRequest { PerformanceId = Guid.Empty, CustomerEmail = "invalid", Seats = 0 },
            createBooking, default);
        var bookingProblem = (bookingResponse.Result as BadRequestObjectResult)?.Value as ValidationProblemDetails;
        Check.That(bookingProblem?.Extensions["code"]?.ToString() == "booking.validation"
            && bookingProblem.Errors.Keys.Order().SequenceEqual(new[] { "CustomerEmail", "IdempotencyKey", "PerformanceId", "Seats" }),
            "Booking slice preserves HTTP 400 and all validation fields before persistence");

        var invalidDetails = new PerformanceDetailsInput("", "", clock.GetUtcNow().AddDays(-1), 0);
        var createResponse = await new PerformancesController().Create(invalidDetails,
            new Performances.CreatePerformance.Handler(new PerformanceRepository(database), detailsValidator, clock), default);
        Check.That((createResponse.Result as BadRequestObjectResult)?.Value is ValidationProblemDetails { Errors.Count: 4 },
            "Create-performance slice preserves validation response before persistence");
        var updateResponse = await new PerformancesController().Update(Guid.NewGuid(), invalidDetails,
            new Performances.UpdatePerformance.Handler(new PerformanceRepository(database), detailsValidator, clock), default);
        Check.That((updateResponse.Result as BadRequestObjectResult)?.Value is ValidationProblemDetails { Errors.Count: 4 },
            "Update-performance slice still validates before loading the performance");
        var cancelResponse = await new PerformancesController().Cancel(Guid.NewGuid(), new CancelPerformanceInput(""),
            new Performances.CancelPerformance.Handler(new PerformanceRepository(database), cancelValidator, clock), default);
        Check.That((cancelResponse.Result as BadRequestObjectResult)?.Value is ValidationProblemDetails problem
            && problem.Extensions["code"]?.ToString() == "performance.cancellation-validation",
            "Cancel-performance slice preserves its validation error contract");

        CheckEndpointDiscoveryAndRoutes();
        Check.That(typeof(BookingsController).GetMethod(nameof(BookingsController.Create))!.GetParameters()
            .Single(parameter => parameter.Name == "idempotencyKey")
            .GetCustomAttribute<FromHeaderAttribute>()?.Name == "Idempotency-Key",
            "Booking idempotency key remains an HTTP header");

        var performance = new PerformanceEntity("Opera", "Hall", clock.GetUtcNow().AddDays(1), 10, clock.GetUtcNow());
        Check.That(performance.Publish(clock.GetUtcNow())
            && !performance.UpdateDraft("Changed", "Hall", performance.StartsAt, 20, clock.GetUtcNow())
            && performance.Cancel("Cancelled", clock.GetUtcNow())
            && !performance.Cancel("Again", clock.GetUtcNow()),
            "Performance publication/edit/cancellation lifecycle is preserved");
        var booking = new OperaHouse.Booking.Models.Booking(Guid.NewGuid(), Guid.NewGuid(),
            performance.Id, "guest@example.com", 2, clock.GetUtcNow(), clock.GetUtcNow().AddMinutes(15));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var bookingJson = JsonSerializer.SerializeToElement(BookingDto.From(booking), options);
        Check.That(bookingJson.EnumerateObject().Select(property => property.Name).SequenceEqual(new[]
            { "id", "performanceId", "customerEmail", "seats", "status", "createdAt", "expiresAt", "confirmedAt", "expiredAt", "cancelledAt" })
            && bookingJson.GetProperty("id").GetGuid() == booking.Id
            && bookingJson.GetProperty("status").GetInt32() == (int)BookingStatus.Pending,
            "Explicit booking mapping preserves response fields, identifiers, and enum representation");
        var performanceJson = JsonSerializer.SerializeToElement(PerformanceDto.From(performance), options);
        Check.That(performanceJson.EnumerateObject().Select(property => property.Name).SequenceEqual(new[]
            { "id", "title", "venue", "startsAt", "capacity", "availableSeats", "status", "createdAt", "updatedAt", "publishedAt", "cancelledAt", "cancellationReason" })
            && performanceJson.GetProperty("cancellationReason").GetString() == "Cancelled",
            "Explicit performance mapping preserves response fields and lifecycle details");
    }

    private static void CheckEndpointDiscoveryAndRoutes()
    {
        var contracts = new (Type Controller, string Action, string Method, string Path, bool Admin)[]
        {
            (typeof(BookingsController), "Create", "POST", "/bookings", false),
            (typeof(BookingsController), "GetById", "GET", "/bookings/{id:guid}", false),
            (typeof(PerformancesController), "Browse", "GET", "/performances", false),
            (typeof(PerformancesController), "GetAll", "GET", "/admin/performances", true),
            (typeof(PerformancesController), "GetById", "GET", "/admin/performances/{id:guid}", true),
            (typeof(PerformancesController), "Create", "POST", "/admin/performances", true),
            (typeof(PerformancesController), "Update", "PUT", "/admin/performances/{id:guid}", true),
            (typeof(PerformancesController), "Publish", "POST", "/admin/performances/{id:guid}/publish", true),
            (typeof(PerformancesController), "Cancel", "POST", "/admin/performances/{id:guid}/cancel", true)
        };
        var manager = new ApplicationPartManager();
        manager.ApplicationParts.Add(new AssemblyPart(typeof(BookingsController).Assembly));
        manager.FeatureProviders.Add(new ControllerFeatureProvider());
        var discovered = new ControllerFeature();
        manager.PopulateFeature(discovered);
        Check.That(discovered.Controllers.Count == 2
            && discovered.Controllers.Contains(typeof(BookingsController).GetTypeInfo())
            && discovered.Controllers.Contains(typeof(PerformancesController).GetTypeInfo()),
            "MVC discovers exactly one Bookings controller and one Performances controller");

        var namedRoutes = new List<string>();
        foreach (var contract in contracts)
        {
            var method = contract.Controller.GetMethod(contract.Action)!;
            var action = method.GetCustomAttribute<HttpMethodAttribute>()!;
            var policy = method.GetCustomAttribute<AuthorizeAttribute>()?.Policy
                ?? contract.Controller.GetCustomAttribute<AuthorizeAttribute>()?.Policy;
            Check.That(action.HttpMethods.Single() == contract.Method && "/" + action.Template == contract.Path
                && policy == (contract.Admin ? OperaHouse.Api.Security.AuthorizationPolicies.Admin : null),
                $"{contract.Controller.Name}.{contract.Action}: HTTP verb, path, and authorization remain intact");
            if (action.Name is not null) namedRoutes.Add(action.Name);
        }

        Check.That(namedRoutes.Count == 2 && namedRoutes.Distinct(StringComparer.Ordinal).Count() == 2
            && typeof(BookingsController).GetMethod("GetById")!.GetCustomAttribute<HttpGetAttribute>()?.Name
                == BookingsController.RouteName
            && typeof(PerformancesController).GetMethod("GetById")!.GetCustomAttribute<HttpGetAttribute>()?.Name
                == PerformancesController.RouteName,
            "Creation responses retain unique named GET routes");
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2027, 1, 1, 12, 0, 0, TimeSpan.Zero);
    }
}
