using System.Collections.Concurrent;

namespace ChatApp.Api.Infrastructure.Presence;

public sealed class HubRateLimiter
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastAccepted = new();
    // Heartbeats must be at least this far apart per connection. Small enough that
    // event-driven heartbeats (active <-> inactive transitions) still get through,
    // large enough to block spam.
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(3);

    public bool TryConsume(string connectionId)
    {
        var now = DateTimeOffset.UtcNow;
        if (_lastAccepted.TryGetValue(connectionId, out var last) && now - last < MinInterval)
        {
            return false;
        }
        _lastAccepted[connectionId] = now;
        return true;
    }

    public void Remove(string connectionId) => _lastAccepted.TryRemove(connectionId, out _);
}
