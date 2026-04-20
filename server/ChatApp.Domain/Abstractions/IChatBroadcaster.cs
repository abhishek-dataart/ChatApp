using ChatApp.Domain.Services.Identity;
using ChatApp.Domain.Services.Messaging;
using ChatApp.Domain.Services.Rooms;
using ChatApp.Domain.Services.Social;

namespace ChatApp.Domain.Abstractions;

public interface IChatBroadcaster
{
    Task BroadcastMessageCreatedToPersonalChatAsync(Guid personalChatId, MessagePayload payload, CancellationToken ct = default);
    Task BroadcastMessageCreatedToRoomAsync(Guid roomId, MessagePayload payload, CancellationToken ct = default);
    Task BroadcastMessageEditedToPersonalChatAsync(Guid chatId, MessagePayload payload, CancellationToken ct = default);
    Task BroadcastMessageEditedToRoomAsync(Guid roomId, MessagePayload payload, CancellationToken ct = default);
    Task BroadcastMessageDeletedToPersonalChatAsync(Guid chatId, MessageDeletedPayload payload, CancellationToken ct = default);
    Task BroadcastMessageDeletedToRoomAsync(Guid roomId, MessageDeletedPayload payload, CancellationToken ct = default);
    Task BroadcastUnreadChangedAsync(Guid userId, UnreadChangedPayload payload, CancellationToken ct = default);

    Task BroadcastRoomMemberChangedAsync(Guid roomId, RoomMemberChangedPayload payload, CancellationToken ct = default);
    Task BroadcastRoomBannedToUserAsync(Guid userId, RoomBannedPayload payload, CancellationToken ct = default);
    Task BroadcastRoomDeletedAsync(Guid roomId, RoomDeletedPayload payload, CancellationToken ct = default);
    Task RemoveConnectionFromRoomAsync(string connectionId, Guid roomId, CancellationToken ct = default);
    Task BroadcastUserDeletedAsync(Guid userId, UserDeletedPayload payload, CancellationToken ct = default);

    Task BroadcastFriendshipChangedAsync(Guid userId, FriendshipChangedPayload payload, CancellationToken ct = default);
    Task BroadcastInvitationChangedAsync(Guid userId, InvitationChangedPayload payload, CancellationToken ct = default);
}
