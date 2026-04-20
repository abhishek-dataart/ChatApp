namespace ChatApp.Api.Contracts.Rooms;

public sealed record MyRoomEntry(
    Guid Id,
    string Name,
    string Description,
    string Visibility,
    int MemberCount,
    int Capacity,
    DateTimeOffset CreatedAt,
    string Role,
    DateTimeOffset JoinedAt,
    string? LogoUrl);
