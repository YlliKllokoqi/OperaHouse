using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace OperaHouse.Notification.Worker;

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

            if (report.Status == HealthStatus.Healthy)
            {
                logger.LogInformation("Worker health check succeeded.");
            }
            else
            {
                logger.LogWarning(
                    "Worker health check returned {HealthStatus}.",
                    report.Status);

                foreach (var entry in report.Entries)
                {
                    logger.LogWarning(
                        entry.Value.Exception,
                        "Health check {HealthCheckName} returned {HealthStatus}: {Description}",
                        entry.Key,
                        entry.Value.Status,
                        entry.Value.Description);
                }
            }

            await Task.Delay(
                TimeSpan.FromSeconds(30),
                stoppingToken);
        }
    }
}
