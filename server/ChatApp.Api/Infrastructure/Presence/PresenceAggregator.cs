using System.Collections.Concurrent;
using ChatApp.Api.Contracts.Presence;
using ChatApp.Api.Hubs;
using ChatApp.Domain.Abstractions;
using ChatApp.Domain.Presence;
using ChatApp.Domain.Services.Presence;
using Microsoft.AspNetCore.SignalR;

namespace ChatApp.Api.Infrastructure.Presence;

public sealed class PresenceAggregator(
    IPresenceStore store,
    IHubContext<PresenceHub> hubContext,
    IServiceScopeFactory scopeFactory,
    ILogger<PresenceAggregator> logger)
{
    private readonly ConcurrentDictionary<Guid, PresenceState> _lastBroadcastState = new();

    public async Task OnConnectedAsync(Guid userId, string connId, CancellationToken ct)
    {
        store.Register(userId, connId);
        await BroadcastIfChangedAsync(userId, ct);
    }

    public async Task OnDisconnectedAsync(Guid userId, string connId, CancellationToken ct)
    {
        store.Unregister(userId, connId);
        await BroadcastIfChangedAsync(userId, ct);
    }

    public async Task HeartbeatAsync(Guid userId, string connId, bool isActive, CancellationToken ct)
    {
        store.Touch(userId, connId, isActive, DateTime.UtcNow);
        // Push state changes (e.g. AFK -> Online) without waiting for the next tick
        // so the spec's <2s presence-update latency is met.
        await BroadcastIfChangedAsync(userId, ct);
    }

    public async Task RecomputeAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var snapshot = store.SnapshotStates(now);

        var changed = new List<(Guid UserId, PresenceState State)>();

        foreach (var (userId, state) in snapshot)
        {
            if (!_lastBroadcastState.TryGetValue(userId, out var prev) || prev != state)
            {
                _lastBroadcastState[userId] = state;
                changed.Add((userId, state));
            }
        }

        // Detect users who went offline (tracked but no longer in store snapshot)
        var trackedIds = _lastBroadcastState.Keys.ToList();
        foreach (var userId in trackedIds)
        {
            if (!snapshot.ContainsKey(userId) && _lastBroadcastState.TryRemove(userId, out _))
            {
                changed.Add((userId, PresenceState.Offline));
            }
        }

        foreach (var (userId, state) in changed)
        {
            await BroadcastToTargetsAsync(userId, state, ct);
        }
    }

    public async Task<IReadOnlyCollection<(Guid UserId, PresenceState State)>> GetContactSnapshotAsync(
        Guid viewerId, CancellationToken ct)
    {
        var targets = await ResolveTargetsAsync(viewerId, ct);
        var now = DateTime.UtcNow;
        var result = new List<(Guid UserId, PresenceState State)>();

        foreach (var contactId in targets)
        {
            var state = store.GetState(contactId, now);
            if (state is not null)
            {
                result.Add((contactId, state.Value));
            }
        }

        return result;
    }

    private async Task BroadcastIfChangedAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var state = store.GetState(userId, now) ?? PresenceState.Offline;

        var hasEntry = _lastBroadcastState.TryGetValue(userId, out var prevState);
        if (hasEntry && state == prevState)
        {
            return;
        }

        if (state == PresenceState.Offline)
        {
            _lastBroadcastState.TryRemove(userId, out _);
        }
        else
        {
            _lastBroadcastState[userId] = state;
        }

        await BroadcastToTargetsAsync(userId, state, ct);
    }

    private async Task BroadcastToTargetsAsync(Guid userId, PresenceState state, CancellationToken ct)
    {
        IReadOnlyCollection<Guid> targets;
        try
        {
            targets = await ResolveTargetsAsync(userId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve presence fan-out targets for userId={UserId}", userId);
            return;
        }

        var payload = new PresenceChangedEvent(userId, StateToString(state));

        foreach (var targetId in targets)
        {
            if (targetId == userId)
            {
                continue;
            }

            try
            {
                await hubContext.Clients
                    .Group($"user:{targetId}")
                    .SendAsync("PresenceChanged", payload, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to broadcast PresenceChanged to targetId={TargetId}", targetId);
            }
        }
    }

    private async Task<IReadOnlyCollection<Guid>> ResolveTargetsAsync(Guid userId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IPresenceFanoutResolver>();
        return await resolver.ResolveTargetsAsync(userId, ct);
    }

    private static string StateToString(PresenceState state) => state switch
    {
        PresenceState.Online => "online",
        PresenceState.Afk => "afk",
        _ => "offline",
    };
}
