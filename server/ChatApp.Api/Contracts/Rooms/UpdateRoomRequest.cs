namespace ChatApp.Api.Contracts.Rooms;

public record UpdateRoomRequest(string? Name, string? Description, string? Visibility);
