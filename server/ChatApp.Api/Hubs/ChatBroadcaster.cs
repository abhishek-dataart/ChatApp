using ChatApp.Api.Contracts.Messages;
using ChatApp.Domain.Abstractions;
using ChatApp.Domain.Services.Identity;
using ChatApp.Domain.Services.Messaging;
using ChatApp.Domain.Services.Rooms;
using ChatApp.Domain.Services.Social;
using Microsoft.AspNetCore.SignalR;

namespace ChatApp.Api.Hubs;

public class ChatBroadcaster(IHubContext<ChatHub> hub) : IChatBroadcaster
{
    public Task BroadcastMessageCreatedToPersonalChatAsync(Guid personalChatId, MessagePayload payload, CancellationToken ct = default) =>
        hub.Clients.Group(ChatGroups.PersonalChat(personalChatId))
            .SendAsync("MessageCreated", MessageResponse.From(payload), ct);

    public Task BroadcastMessageCreatedToRoomAsync(Guid roomId, MessagePayload payload, CancellationToken ct = default) =>
        hub.Clients.Group(ChatGroups.Room(roomId))
            .SendAsync("MessageCreated", MessageResponse.From(payload), ct);

    public Task BroadcastMessageEditedToPersonalChatAsync(Guid chatId, MessagePayload payload, CancellationToken ct = default) =>
        hub.Clients.Group(ChatGroups.PersonalChat(chatId))
            .SendAsync("MessageEdited", MessageResponse.From(payload), ct);

    public Task BroadcastMessageEditedToRoomAsync(Guid roomId, MessagePayload payload, CancellationToken ct = default) =>
        hub.Clients.Group(ChatGroups.Room(roomId))
            .SendAsync("MessageEdited", MessageResponse.From(payload), ct);

    public Task BroadcastMessageDeletedToPersonalChatAsync(Guid chatId, MessageDeletedPayload payload, CancellationToken ct = default) =>
        hub.Clients.Group(ChatGroups.PersonalChat(chatId))
            .SendAsync("MessageDeleted", new MessageDeletedResponse(payload.Id, payload.Scope, payload.PersonalChatId, payload.RoomId), ct);

    public Task BroadcastMessageDeletedToRoomAsync(Guid roomId, MessageDeletedPayload payload, CancellationToken ct = default) =>
        hub.Clients.Group(ChatGroups.Room(roomId))
            .SendAsync("MessageDeleted", new MessageDeletedResponse(payload.Id, payload.Scope, payload.PersonalChatId, payload.RoomId), ct);

    public Task BroadcastUnreadChangedAsync(Guid userId, UnreadChangedPayload payload, CancellationToken ct = default) =>
        hub.Clients.Group($"user:{userId}")
            .SendAsync("UnreadChanged", new UnreadResponse(payload.Scope, payload.ScopeId, payload.UnreadCount), ct);

    public Task BroadcastRoomMemberChangedAsync(Guid roomId, RoomMemberChangedPayload payload, CancellationToken ct = default) =>
        hub.Clients.Group(ChatGroups.Room(roomId))
            .SendAsync("RoomMemberChanged", payload, ct);

    public Task BroadcastRoomBannedToUserAsync(Guid userId, RoomBannedPayload payload, CancellationToken ct = default) =>
        hub.Clients.Group($"user:{userId}")
            .SendAsync("RoomBanned", payload, ct);

    public Task BroadcastRoomDeletedAsync(Guid roomId, RoomDeletedPayload payload, CancellationToken ct = default) =>
        hub.Clients.Group(ChatGroups.Room(roomId))
            .SendAsync("RoomDeleted", payload, ct);

    public Task RemoveConnectionFromRoomAsync(string connectionId, Guid roomId, CancellationToken ct = default) =>
        hub.Groups.RemoveFromGroupAsync(connectionId, ChatGroups.Room(roomId), ct);

    public Task BroadcastUserDeletedAsync(Guid userId, UserDeletedPayload payload, CancellationToken ct = default) =>
        hub.Clients.All.SendAsync("UserDeleted", payload, ct);

    public Task BroadcastFriendshipChangedAsync(Guid userId, FriendshipChangedPayload payload, CancellationToken ct = default) =>
        hub.Clients.Group($"user:{userId}")
            .SendAsync("FriendshipChanged", payload, ct);

    public Task BroadcastInvitationChangedAsync(Guid userId, InvitationChangedPayload payload, CancellationToken ct = default) =>
        hub.Clients.Group($"user:{userId}")
            .SendAsync("InvitationChanged", payload, ct);
}
