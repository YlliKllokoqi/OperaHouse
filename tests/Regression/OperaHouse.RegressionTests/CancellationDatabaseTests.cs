using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using OperaHouse.Api.Controllers;
using OperaHouse.Booking.Features.ExpireBookings;
using OperaHouse.Booking.Models;
using OperaHouse.Booking.Persistence;
using OperaHouse.Contracts.Events;
using OperaHouse.Messaging;
using OperaHouse.Persistence;
using OperaHouse.Persistence.Bookings;
using CancelBooking = OperaHouse.Booking.Features.CancelBooking.Handler;
using BookingEntity = OperaHouse.Booking.Models.Booking;
using PerformanceEntity = OperaHouse.Performance.Models.Performance;

namespace OperaHouse.RegressionTests;

internal static class CancellationDatabaseTests
{
    public static async Task RunAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("OPERAHOUSE_CANCELLATION_TEST_DB")
            ?? throw new InvalidOperationException("Set OPERAHOUSE_CANCELLATION_TEST_DB to an empty test database.");
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        if (connection.Database?.StartsWith("operahouse_cancel_test", StringComparison.Ordinal) != true)
        {
            throw new InvalidOperationException("Use a disposable database named operahouse_cancel_test or with that prefix.");
        }

        BookingDbContext OpenDatabase(bool failSave = false)
        {
            var options = new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(connection.ConnectionString);
            if (failSave) options.AddInterceptors(new RejectSave());
            return new BookingDbContext(options.Options);
        }

        await using var database = OpenDatabase();
        if (!await database.Database.EnsureCreatedAsync())
        {
            throw new InvalidOperationException("The test database must be empty before running these checks.");
        }

        var now = new DateTimeOffset(2027, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FixedClock(now);
        var repository = CreateRepository(database);
        var first = await ReserveAsync();
        var controller = new BookingsController
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var handler = new CancelBooking(repository, clock);
        var dev = EnvironmentStub(Environments.Development);
        controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Loopback;
        var production = await controller.Cancel(first.Id, handler, EnvironmentStub(Environments.Production), default);
        controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        var remote = await controller.Cancel(first.Id, handler, dev, default);
        controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Loopback;
        Check.That(production.Result is NotFoundResult && remote.Result is NotFoundResult
            && (await repository.GetByIdAsync(first.Id, default))?.Status == BookingStatus.Pending,
            "Cancellation is available only to loopback requests in Development");

        var invalid = await controller.Cancel(Guid.Empty, handler, dev, default);
        var unknown = await controller.Cancel(Guid.NewGuid(), handler, dev, default);
        var due = await controller.Cancel(first.Id, new CancelBooking(repository, new FixedClock(first.ExpiresAt)), dev, default);
        Check.That(invalid.Result is BadRequestObjectResult && unknown.Result is ObjectResult { StatusCode: 404 }
            && due.Result is ObjectResult { StatusCode: 409 },
            "Cancellation rejects empty IDs, unknown IDs and reservations at their deadline");

        await using var duplicateDb = OpenDatabase();
        var cancelRequest = controller.Cancel(first.Id, handler, dev, default);
        var duplicateRequest = CreateRepository(duplicateDb).TryCancelAsync(first.Id, now, default);
        await Task.WhenAll(cancelRequest, duplicateRequest);
        var cancelled = await cancelRequest;
        var repeated = await controller.Cancel(first.Id, handler, dev, default);
        Check.That(cancelled.Result is OkObjectResult { Value: BookingDto result }
            && result.Status == BookingStatus.Cancelled && result.CancelledAt == now
            && repeated.Result is OkObjectResult { Value: BookingDto repeat } && repeat == result
            && (await duplicateRequest).Outcome == BookingCancellationOutcome.Cancelled
            && await AvailableSeatsAsync(first) == 10,
            "Concurrent and repeated cancellations return success and release seats once");
        var outbox = await database.OutboxMessages.AsNoTracking().SingleAsync(message => message.Type == nameof(BookingCancelled));
        var payload = JsonSerializer.Deserialize<BookingCancelled>(outbox.Payload)!;
        Check.That(outbox.RoutingKey == "booking.cancelled" && payload.BookingId == first.Id
            && payload.Seats == first.Seats && payload.CustomerEmail == first.CustomerEmail && payload.OccurredAt == now
            && payload.MessageId == outbox.MessageId && payload.CorrelationId == outbox.CorrelationId,
            "Cancellation stores exactly one outbox event with the stored booking details and stable identifiers");

        var race = await ReserveAsync();
        await using var cancellationDb = OpenDatabase();
        await using var expirationDb = OpenDatabase();
        var expirationInput = new ExpiringBooking(race.Id, race.PerformanceId, race.CustomerEmail, race.Seats);
        var expirationEvent = new BookingExpired(Guid.NewGuid(), Guid.NewGuid(), race.Id, race.PerformanceId,
            race.CustomerEmail, race.Seats, race.ExpiresAt);
        var cancelTask = CreateRepository(cancellationDb).TryCancelAsync(race.Id, now, default);
        var expireTask = CreateRepository(expirationDb).TryExpireAsync(expirationInput, expirationEvent, race.ExpiresAt, default);
        await Task.WhenAll(cancelTask, expireTask);
        var expired = await expireTask;
        var winner = await repository.GetByIdAsync(race.Id, default);
        var terminalEvents = await database.OutboxMessages.AsNoTracking()
            .Where(message => message.Type == nameof(BookingCancelled) || message.Type == nameof(BookingExpired))
            .Select(message => new { message.Type, message.Payload }).ToListAsync();
        var raceEvent = terminalEvents.Single(message => message.Payload.Contains(race.Id.ToString(), StringComparison.Ordinal));
        Check.That((await cancelTask).Outcome == (expired ? BookingCancellationOutcome.Ineligible : BookingCancellationOutcome.Cancelled)
            && winner?.Status == (expired ? BookingStatus.Expired : BookingStatus.Cancelled)
            && raceEvent.Type == (expired ? nameof(BookingExpired) : nameof(BookingCancelled))
            && await AvailableSeatsAsync(race) == 10,
            "Cancellation racing expiration produces one terminal event and one seat release");

        var rollback = await ReserveAsync();
        await using var failingDb = OpenDatabase(failSave: true);
        var error = await Check.ThrowsAsync<InvalidOperationException>(
            () => CreateRepository(failingDb).TryCancelAsync(rollback.Id, now, default));
        var rolledBack = await repository.GetByIdAsync(rollback.Id, default);
        Check.That(error.Message == RejectSave.Message && rolledBack is { Status: BookingStatus.Pending, CancelledAt: null }
            && await AvailableSeatsAsync(rollback) == 8
            && await database.OutboxMessages.CountAsync(message =>
                message.Type == nameof(BookingCancelled) || message.Type == nameof(BookingExpired)) == 2,
            "An outbox save failure rolls back both cancellation and seat release");

        async Task<BookingEntity> ReserveAsync()
        {
            var performance = new PerformanceEntity("Cancellation test", "Test Hall", now.AddDays(1), 10, now);
            performance.Publish(now);
            database.Performances.Add(performance);
            await database.SaveChangesAsync();
            database.ChangeTracker.Clear();
            var booking = new BookingEntity(Guid.NewGuid(), Guid.NewGuid(), performance.Id, "guest@example.com",
                2, now, now.AddMinutes(15));
            var created = new BookingCreated(Guid.NewGuid(), Guid.NewGuid(), booking.Id, booking.PerformanceId,
                booking.CustomerEmail, booking.Seats, booking.ExpiresAt, now);
            var result = await repository.TryCreateAsync(booking, created, now, default);
            if (result.Outcome != BookingCreationOutcome.Created) throw new InvalidOperationException("Could not seed a reservation.");
            database.ChangeTracker.Clear();
            return booking;
        }

        Task<int> AvailableSeatsAsync(BookingEntity booking) => database.Performances
            .Where(performance => performance.Id == booking.PerformanceId).Select(performance => performance.AvailableSeats)
            .SingleAsync();
    }

    private static BookingRepository CreateRepository(BookingDbContext database) =>
        new(database, Options.Create(new RabbitMqOptions()));

    private static IWebHostEnvironment EnvironmentStub(string name) =>
        InterfaceStub.Create<IWebHostEnvironment>((method, _) => method.Name == "get_EnvironmentName"
            ? name : throw new NotSupportedException(method.Name));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RejectSave : SaveChangesInterceptor
    {
        public const string Message = "Injected cancellation outbox save failure.";

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<InterceptionResult<int>>(new InvalidOperationException(Message));
    }
}
