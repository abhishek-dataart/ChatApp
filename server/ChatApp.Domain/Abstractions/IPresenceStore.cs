using ChatApp.Domain.Presence;

namespace ChatApp.Domain.Abstractions;

public interface IPresenceStore
{
    void Register(Guid userId, string connectionId);
    void Unregister(Guid userId, string connectionId);
    void Touch(Guid userId, string connectionId, bool isActive, DateTime nowUtc);
    IReadOnlyDictionary<Guid, PresenceState> SnapshotStates(DateTime nowUtc);
    PresenceState? GetState(Guid userId, DateTime nowUtc);
    IEnumerable<string> GetConnectionIds(Guid userId);
}
