using ChatApp.Api.Contracts.Rooms;
using ChatApp.Api.Contracts.Social;
using ChatApp.Data.Entities.Rooms;
using ChatApp.Data.Services.Rooms;

namespace ChatApp.Api.Controllers.Rooms;

internal static class RoomsMappings
{
    internal static RoomDetailResponse ToDetailResponse(RoomDetailOutcome o)
    {
        var ownerRow = o.Members.FirstOrDefault(m => m.Role == RoomRole.Owner);
        var ownerSummary = ownerRow is not null
            ? new UserSummary(ownerRow.UserId, ownerRow.Username, ownerRow.DisplayName, ownerRow.AvatarUrl)
            : new UserSummary(o.OwnerId, string.Empty, string.Empty, null);

        var members = o.Members.Select(m => new RoomMemberEntry(
            new UserSummary(m.UserId, m.Username, m.DisplayName, m.AvatarUrl),
            m.Role.ToString().ToLowerInvariant(),
            m.JoinedAt)).ToList();

        return new RoomDetailResponse(
            o.Id, o.Name, o.Description, o.Visibility.ToString().ToLowerInvariant(),
            o.MemberCount, o.Capacity, o.CreatedAt,
            ownerSummary, members, o.CurrentUserRole.ToString().ToLowerInvariant(),
            o.LogoUrl);
    }
}
