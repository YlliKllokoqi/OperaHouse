using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace OperaHouse.Messaging;

public class RabbitMqHealthCheck(IOptions<RabbitMqOptions> options) : IHealthCheck
{
    private readonly RabbitMqOptions _options = options.Value;
    
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password
            };

            await using var connection = await factory.CreateConnectionAsync(cancellationToken);
            await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
            
            return HealthCheckResult.Healthy("RabbitMQ is reachable.");
        }
        catch (Exception e)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ is not reachable.", e);
        }
    }
}