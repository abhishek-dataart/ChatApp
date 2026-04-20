using ChatApp.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Api.Hubs;

[Authorize]
public class ChatHub(ChatDbContext db, ILogger<ChatHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userIdString = Context.UserIdentifier!;
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userIdString}");

        var pchatCount = 0;
        var roomCount = 0;
        if (Guid.TryParse(userIdString, out var userId))
        {
            var pchatIds = await db.PersonalChats
                .AsNoTracking()
                .Where(p => p.UserAId == userId || p.UserBId == userId)
                .Select(p => p.Id)
                .ToListAsync(Context.ConnectionAborted);

            foreach (var id in pchatIds)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, ChatGroups.PersonalChat(id));
            }
            pchatCount = pchatIds.Count;

            var roomIds = await db.RoomMembers
                .AsNoTracking()
                .Where(m => m.UserId == userId)
                .Select(m => m.RoomId)
                .ToListAsync(Context.ConnectionAborted);

            foreach (var rid in roomIds)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, ChatGroups.Room(rid));
            }
            roomCount = roomIds.Count;
        }

        logger.LogInformation(
            "ChatHub connected userId={UserId} connectionId={ConnectionId} joined {PchatCount} pchat groups, {RoomCount} room groups",
            userIdString, Context.ConnectionId, pchatCount, roomCount);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogInformation(
            "ChatHub disconnected userId={UserId} connectionId={ConnectionId}",
            Context.UserIdentifier, Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinRoomGroup(Guid roomId)
    {
        if (!Guid.TryParse(Context.UserIdentifier, out var userId)) return;
        var isMember = await db.RoomMembers
            .AsNoTracking()
            .AnyAsync(m => m.UserId == userId && m.RoomId == roomId, Context.ConnectionAborted);
        if (!isMember) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, ChatGroups.Room(roomId));
    }

    public Task LeaveRoomGroup(Guid roomId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, ChatGroups.Room(roomId));

    public async Task JoinPersonalChatGroup(Guid chatId)
    {
        if (!Guid.TryParse(Context.UserIdentifier, out var userId)) return;
        var isParticipant = await db.PersonalChats
            .AsNoTracking()
            .AnyAsync(p => p.Id == chatId && (p.UserAId == userId || p.UserBId == userId), Context.ConnectionAborted);
        if (!isParticipant) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, ChatGroups.PersonalChat(chatId));
    }
}
