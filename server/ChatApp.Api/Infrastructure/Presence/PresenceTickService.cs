namespace ChatApp.Api.Infrastructure.Presence;

public sealed class PresenceTickService(PresenceAggregator aggregator, ILogger<PresenceTickService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await aggregator.RecomputeAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "PresenceTickService: error during recompute tick");
            }
        }
    }
}
