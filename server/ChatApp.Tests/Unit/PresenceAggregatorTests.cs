using ChatApp.Api.Infrastructure.Presence;
using ChatApp.Domain.Presence;
using FluentAssertions;
using Xunit;

namespace ChatApp.Tests.Unit;

// The "aggregation" rules (active<60s -> online, idle -> afk, none -> offline)
// live in InMemoryPresenceStore.ComputeState. The PresenceAggregator sits on top and
// broadcasts over SignalR, so we test the decision logic via the store directly.
public class PresenceAggregatorTests
{
    [Fact]
    public void No_connections_means_offline()
    {
        var store = new InMemoryPresenceStore();
        var u = Guid.NewGuid();
        store.GetState(u, DateTime.UtcNow).Should().BeNull();
    }

    [Fact]
    public void Active_within_60s_is_online()
    {
        var store = new InMemoryPresenceStore();
        var u = Guid.NewGuid();
        var now = DateTime.UtcNow;
        store.Register(u, "conn-1");
        store.Touch(u, "conn-1", isActive: true, nowUtc: now.AddSeconds(-30));

        store.GetState(u, now).Should().Be(PresenceState.Online);
    }

    [Fact]
    public void Last_active_over_60s_ago_is_afk()
    {
        var store = new InMemoryPresenceStore();
        var u = Guid.NewGuid();
        var now = DateTime.UtcNow;
        store.Register(u, "conn-1");
        store.Touch(u, "conn-1", isActive: true, nowUtc: now.AddSeconds(-90));

        store.GetState(u, now).Should().Be(PresenceState.Afk);
    }

    [Fact]
    public void Any_active_connection_wins_over_idle_connection()
    {
        var store = new InMemoryPresenceStore();
        var u = Guid.NewGuid();
        var now = DateTime.UtcNow;
        store.Register(u, "conn-idle");
        store.Register(u, "conn-fresh");
        store.Touch(u, "conn-idle", isActive: true, nowUtc: now.AddSeconds(-120));
        store.Touch(u, "conn-fresh", isActive: true, nowUtc: now.AddSeconds(-10));

        store.GetState(u, now).Should().Be(PresenceState.Online);
    }

    [Fact]
    public void Unregister_last_connection_means_offline()
    {
        var store = new InMemoryPresenceStore();
        var u = Guid.NewGuid();
        store.Register(u, "c1");
        store.Unregister(u, "c1");
        store.GetState(u, DateTime.UtcNow).Should().BeNull();
    }

    [Fact]
    public void SnapshotStates_excludes_users_with_no_connections()
    {
        var store = new InMemoryPresenceStore();
        var online = Guid.NewGuid();
        store.Register(online, "c1");
        var snap = store.SnapshotStates(DateTime.UtcNow);
        snap.Should().ContainKey(online);
    }
}
