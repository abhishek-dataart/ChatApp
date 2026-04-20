using ChatApp.Api.Contracts.Social;

namespace ChatApp.Api.Contracts.Rooms;

public sealed record InvitationEntry(
    Guid InvitationId,
    RoomSummary Room,
    UserSummary Inviter,
    string? Note,
    DateTimeOffset CreatedAt);
