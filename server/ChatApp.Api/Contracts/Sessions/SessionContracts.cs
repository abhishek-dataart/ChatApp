namespace ChatApp.Api.Contracts.Sessions;

public record SessionView(Guid Id, string UserAgent, string Ip, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, bool IsCurrent);
