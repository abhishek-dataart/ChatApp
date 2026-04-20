using ChatApp.Api.Contracts.Social;

namespace ChatApp.Api.Contracts.Rooms;

public sealed record OutgoingInvitationEntry(
    Guid InvitationId,
    UserSummary Invitee,
    UserSummary Inviter,
    string? Note,
    DateTimeOffset CreatedAt);
