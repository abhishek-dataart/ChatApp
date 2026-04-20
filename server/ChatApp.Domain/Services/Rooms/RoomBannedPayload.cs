namespace ChatApp.Domain.Services.Rooms;

public sealed record UserSummaryPayload(Guid Id, string Username, string DisplayName, string? AvatarUrl);

public sealed record RoomBannedPayload(Guid RoomId, string RoomName, UserSummaryPayload BannedBy, DateTimeOffset CreatedAt);
