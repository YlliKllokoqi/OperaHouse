using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OperaHouse.Booking.Application.Bookings;
using OperaHouse.Booking.Domain.Bookings;
using OperaHouse.Contracts.Events;

namespace OperaHouse.Booking.Infrastructure.Bookings;

public sealed class BookingExpirationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<BookingOptions> bookingOptions,
    TimeProvider timeProvider,
    ILogger<BookingExpirationWorker> logger) : BackgroundService
{
    private readonly BookingOptions _options = bookingOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("BOoking expiration worker started.");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ExpireBookingsAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Booking expiration cycle failed.");
            }
            
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(
                        _options
                            .ExpirationCheckIntervalSeconds),
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
        {
            
        }
    }

    private async Task ExpireBookingsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ExpiringBooking> candidates;

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider
                .GetRequiredService<IBookingRepository>();

            candidates =
                await repository.GetExpiringBookingAsync(
                    timeProvider.GetUtcNow(),
                    _options.ExpirationBatchSize,
                    cancellationToken);
        }

        foreach (var candidate in candidates)
        {
            await using var scope =
                scopeFactory.CreateAsyncScope();

            var repository = scope.ServiceProvider
                .GetRequiredService<IBookingRepository>();
            
            var currentTime = timeProvider.GetUtcNow();
            
            var bookingExpired = new BookingExpired(
                MessageId: Guid.NewGuid(),
                CorrelationId: Guid.NewGuid(),
                BookingId: candidate.BookingId,
                PerformanceId:
                candidate.PerformanceId,
                Seats: candidate.Seats,
                OccuredAt: currentTime);
            
            var expired =
                await repository.TryExpireAsync(
                    candidate,
                    bookingExpired,
                    currentTime,
                    cancellationToken);

            if (expired)
            {
                logger.LogInformation(
                    "Expired booking {BookingId} and released {Seats} seats.",
                    candidate.BookingId,
                    candidate.Seats);
            }
        }
    }
}