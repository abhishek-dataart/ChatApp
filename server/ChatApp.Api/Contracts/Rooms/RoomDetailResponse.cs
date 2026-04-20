using ChatApp.Api.Contracts.Social;

namespace ChatApp.Api.Contracts.Rooms;

public sealed record RoomDetailResponse(
    Guid Id,
    string Name,
    string Description,
    string Visibility,
    int MemberCount,
    int Capacity,
    DateTimeOffset CreatedAt,
    UserSummary Owner,
    List<RoomMemberEntry> Members,
    string CurrentUserRole,
    string? LogoUrl);
