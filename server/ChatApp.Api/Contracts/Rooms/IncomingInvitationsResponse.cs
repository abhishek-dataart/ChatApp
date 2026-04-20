namespace ChatApp.Api.Contracts.Rooms;

public sealed record IncomingInvitationsResponse(List<InvitationEntry> Incoming);
