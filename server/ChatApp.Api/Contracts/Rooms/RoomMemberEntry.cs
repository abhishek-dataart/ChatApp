using ChatApp.Api.Contracts.Social;

namespace ChatApp.Api.Contracts.Rooms;

public sealed record RoomMemberEntry(UserSummary User, string Role, DateTimeOffset JoinedAt);
