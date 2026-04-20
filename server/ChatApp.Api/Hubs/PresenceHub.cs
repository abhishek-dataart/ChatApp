using ChatApp.Api.Contracts.Presence;
using ChatApp.Api.Infrastructure.Presence;
using ChatApp.Domain.Presence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ChatApp.Api.Hubs;

[Authorize]
public class PresenceHub(PresenceAggregator aggregator, HubRateLimiter rateLimiter, ILogger<PresenceHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Guid.Parse(Context.UserIdentifier!);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{Context.UserIdentifier}");
        logger.LogInformation(
            "PresenceHub connected userId={UserId} connectionId={ConnectionId}",
            Context.UserIdentifier, Context.ConnectionId);

        await aggregator.OnConnectedAsync(userId, Context.ConnectionId, Context.ConnectionAborted);

        var snapshot = await aggregator.GetContactSnapshotAsync(userId, Context.ConnectionAborted);
        var entries = snapshot
            .Select(e => new PresenceSnapshotEntry(e.UserId, StateToString(e.State)))
            .ToList();
        await Clients.Caller.SendAsync("PresenceSnapshot", new PresenceSnapshotEvent(entries), Context.ConnectionAborted);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Guid.Parse(Context.UserIdentifier!);
        rateLimiter.Remove(Context.ConnectionId);
        // CancellationToken.None: ConnectionAborted is already fired by the time this runs
        await aggregator.OnDisconnectedAsync(userId, Context.ConnectionId, CancellationToken.None);
        logger.LogInformation(
            "PresenceHub disconnected userId={UserId} connectionId={ConnectionId}",
            Context.UserIdentifier, Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public Task Heartbeat(bool isActive)
    {
        if (!rateLimiter.TryConsume(Context.ConnectionId))
        {
            logger.LogWarning("Heartbeat rate-limit exceeded for connection {ConnectionId}", Context.ConnectionId);
            return Task.CompletedTask;
        }
        return aggregator.HeartbeatAsync(
            Guid.Parse(Context.UserIdentifier!),
            Context.ConnectionId,
            isActive,
            Context.ConnectionAborted);
    }

    private static string StateToString(PresenceState state) => state switch
    {
        PresenceState.Online => "online",
        PresenceState.Afk => "afk",
        _ => "offline",
    };
}
