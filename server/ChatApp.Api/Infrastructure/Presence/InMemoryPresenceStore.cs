using System.Collections.Concurrent;
using ChatApp.Domain.Abstractions;
using ChatApp.Domain.Presence;

namespace ChatApp.Api.Infrastructure.Presence;

public sealed class InMemoryPresenceStore : IPresenceStore
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, ConnectionState>> _map = new();

    public void Register(Guid userId, string connectionId)
    {
        var conns = _map.GetOrAdd(userId, _ => new ConcurrentDictionary<string, ConnectionState>());
        conns[connectionId] = new ConnectionState(DateTime.UtcNow, IsActive: true);
    }

    public void Unregister(Guid userId, string connectionId)
    {
        if (!_map.TryGetValue(userId, out var conns))
        {
            return;
        }

        conns.TryRemove(connectionId, out _);
        if (conns.IsEmpty)
        {
            _map.TryRemove(userId, out _);
        }
    }

    public void Touch(Guid userId, string connectionId, bool isActive, DateTime nowUtc)
    {
        if (!_map.TryGetValue(userId, out var conns))
        {
            return;
        }

        conns.AddOrUpdate(
            connectionId,
            _ => new ConnectionState(nowUtc, isActive),
            (_, existing) => isActive
                ? new ConnectionState(nowUtc, true)
                : existing with { IsActive = false });
    }

    public IReadOnlyDictionary<Guid, PresenceState> SnapshotStates(DateTime nowUtc)
    {
        var result = new Dictionary<Guid, PresenceState>();
        foreach (var (userId, conns) in _map)
        {
            var state = ComputeState(conns, nowUtc);
            if (state is not null)
            {
                result[userId] = state.Value;
            }
        }
        return result;
    }

    public PresenceState? GetState(Guid userId, DateTime nowUtc)
    {
        if (!_map.TryGetValue(userId, out var conns) || conns.IsEmpty)
        {
            return null;
        }
        return ComputeState(conns, nowUtc);
    }

    public IEnumerable<string> GetConnectionIds(Guid userId)
    {
        if (!_map.TryGetValue(userId, out var conns))
        {
            return Enumerable.Empty<string>();
        }
        return conns.Keys.ToList();
    }

    private static PresenceState? ComputeState(ConcurrentDictionary<string, ConnectionState> conns, DateTime nowUtc)
    {
        if (conns.IsEmpty)
        {
            return null;
        }

        var threshold = nowUtc.AddSeconds(-60);
        foreach (var (_, s) in conns)
        {
            if (s.LastActiveAt >= threshold)
            {
                return PresenceState.Online;
            }
        }
        return PresenceState.Afk;
    }
}
