namespace ChatApp.Domain.Presence;

public record ConnectionState(DateTime LastActiveAt, bool IsActive);
