namespace ChatApp.Api.Contracts.Social;

public sealed record UserSummary(Guid Id, string Username, string DisplayName, string? AvatarUrl);
