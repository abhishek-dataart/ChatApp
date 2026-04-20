namespace ChatApp.Api.Contracts.Presence;

public record PresenceChangedEvent(Guid UserId, string State);
