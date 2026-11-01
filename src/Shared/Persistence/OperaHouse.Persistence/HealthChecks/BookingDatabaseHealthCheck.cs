using Microsoft.Extensions.Diagnostics.HealthChecks;
using OperaHouse.Persistence;

namespace OperaHouse.Persistence.HealthChecks;

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
