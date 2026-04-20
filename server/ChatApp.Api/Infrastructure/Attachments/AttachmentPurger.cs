using ChatApp.Data.Services.Attachments;
using ChatApp.Domain.Services.Attachments;
using Microsoft.Extensions.Options;

namespace ChatApp.Api.Infrastructure.Attachments;

public sealed class AttachmentPurger(
    IServiceScopeFactory scopeFactory,
    IOptions<AttachmentsOptions> options,
    ILogger<AttachmentPurger> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(
                    TimeSpan.FromMinutes(options.Value.PurgeIntervalMinutes),
                    stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<AttachmentService>();
                await svc.PurgeOnceAsync(stoppingToken);
                logger.LogDebug("Attachment purge completed.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Attachment purge tick failed.");
            }
        }
    }
}
