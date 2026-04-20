namespace ChatApp.Api.Contracts.Presence;

public record PresenceSnapshotEntry(Guid UserId, string State);

public record PresenceSnapshotEvent(IReadOnlyCollection<PresenceSnapshotEntry> Entries);
