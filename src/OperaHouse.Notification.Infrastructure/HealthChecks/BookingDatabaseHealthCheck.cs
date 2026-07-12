using Microsoft.Extensions.Diagnostics.HealthChecks;
using OperaHouse.Booking.Infrastructure.Persistence;

namespace OperaHouse.Notification.Infrastructure.HealthChecks;

public class BookingDatabaseHealthCheck(BookingDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy("Booking database is reachable")
                : HealthCheckResult.Unhealthy("Booking database is not reachable");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Booking database is not reachable", exception);
        }
    }
}