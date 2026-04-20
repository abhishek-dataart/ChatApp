namespace ChatApp.Api.Contracts.Rooms;

public sealed record SendInvitationRequest(string Username, string? Note);
