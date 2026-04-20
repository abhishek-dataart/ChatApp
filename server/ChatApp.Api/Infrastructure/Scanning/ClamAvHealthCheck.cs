using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using nClam;

namespace ChatApp.Api.Infrastructure.Scanning;

public sealed class ClamAvHealthCheck(IOptions<ClamAvOptions> opts) : IHealthCheck
{
    private readonly ClamAvOptions _opts = opts.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var client = new ClamClient(_opts.Host, _opts.Port);
            var ok = await client.PingAsync(ct);
            return ok ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("clamd did not respond to PING");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("clamd unreachable", ex);
        }
    }
}
