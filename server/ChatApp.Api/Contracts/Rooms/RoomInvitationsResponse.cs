namespace ChatApp.Api.Contracts.Rooms;

public sealed record RoomInvitationsResponse(List<OutgoingInvitationEntry> Invitations);
