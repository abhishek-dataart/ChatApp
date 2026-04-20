using ChatApp.Data.Services.Rooms;

namespace ChatApp.Api.Infrastructure.Rooms;

public sealed class SoftDeletedRoomPurger(
    IServiceScopeFactory scopeFactory,
    ILogger<SoftDeletedRoomPurger> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MinRoomAge = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TickInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<RoomPurgeService>();
                await svc.PurgeOnceAsync(MinRoomAge, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Soft-deleted room purge tick failed.");
            }
        }
    }
}
