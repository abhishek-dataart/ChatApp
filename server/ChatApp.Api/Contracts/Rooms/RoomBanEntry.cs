using ChatApp.Api.Contracts.Social;

namespace ChatApp.Api.Contracts.Rooms;

public sealed record RoomBanEntry(Guid BanId, UserSummary User, UserSummary BannedBy, DateTimeOffset CreatedAt);
