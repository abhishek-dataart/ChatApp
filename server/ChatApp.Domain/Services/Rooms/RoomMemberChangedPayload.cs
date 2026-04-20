namespace ChatApp.Domain.Services.Rooms;

public sealed record RoomMemberChangedPayload(Guid RoomId, Guid UserId, string Change, string? Role);
