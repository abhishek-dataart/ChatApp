namespace ChatApp.Api.Contracts.Social;

public sealed record FriendSummary(
    Guid FriendshipId,
    Guid PersonalChatId,
    UserSummary User,
    DateTimeOffset AcceptedAt);
