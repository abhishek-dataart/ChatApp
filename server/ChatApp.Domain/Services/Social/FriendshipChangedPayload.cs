namespace ChatApp.Domain.Services.Social;

public sealed record FriendshipChangedPayload(Guid FriendshipId, string Kind);
