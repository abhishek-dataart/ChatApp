namespace ChatApp.Api.Contracts.Social;

public sealed record PendingFriendship(
    Guid FriendshipId,
    UserSummary User,
    string? Note,
    DateTimeOffset CreatedAt);
