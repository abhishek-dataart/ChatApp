namespace ChatApp.Api.Contracts.Rooms;

public sealed record CatalogEntry(
    Guid Id,
    string Name,
    string Description,
    string Visibility,
    int MemberCount,
    int Capacity,
    DateTimeOffset CreatedAt,
    bool IsMember,
    string? LogoUrl);
