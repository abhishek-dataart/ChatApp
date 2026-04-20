namespace ChatApp.Domain.Services.Rooms;

public sealed record InvitationChangedPayload(Guid InvitationId, string Kind);
