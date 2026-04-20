namespace ChatApp.Api.Contracts.Social;

public sealed record SendFriendRequestRequest(string Username, string? Note);
