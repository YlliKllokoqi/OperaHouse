using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OperaHouse.Notification.Infrastructure.Persistence;

namespace OperaHouse.Notification.Infrastructure.HealthChecks;

public sealed class NotificationDatabaseHealthCheck(NotificationDbContext dbContext)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy("Notification database is reachable.")
                : HealthCheckResult.Unhealthy("Notification database is not reachable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "Notification database is not reachable.",
                exception);
        }
    }
}