namespace ChatApp.Api.Contracts.Rooms;

public sealed record CreateRoomRequest(string Name, string Description, string Visibility, int? Capacity);
