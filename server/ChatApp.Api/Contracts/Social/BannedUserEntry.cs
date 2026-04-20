namespace ChatApp.Api.Contracts.Social;

public sealed record BannedUserEntry(Guid BanId, UserSummary User, DateTimeOffset CreatedAt);
