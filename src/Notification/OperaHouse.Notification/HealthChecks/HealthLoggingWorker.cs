using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace OperaHouse.Notification.HealthChecks;

public sealed class HealthLoggingWorker(
    HealthCheckService healthCheckService,
    ILogger<HealthLoggingWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var report = await healthCheckService.CheckHealthAsync(stoppingToken);

            foreach (var entry in report.Entries.Where(entry => entry.Value.Status != HealthStatus.Healthy))
            {
                logger.LogWarning(
                    entry.Value.Exception,
                    "Health check {HealthCheckName} returned {HealthStatus}: {Description}",
                    entry.Key,
                    entry.Value.Status,
                    entry.Value.Description);
            }

            await Task.Delay(
                TimeSpan.FromSeconds(30),
                stoppingToken);
        }
    }
}
