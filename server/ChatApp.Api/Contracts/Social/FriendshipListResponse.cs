namespace ChatApp.Api.Contracts.Social;

public sealed record FriendshipListResponse(
    List<FriendSummary> Friends,
    List<PendingFriendship> Incoming,
    List<PendingFriendship> Outgoing);
