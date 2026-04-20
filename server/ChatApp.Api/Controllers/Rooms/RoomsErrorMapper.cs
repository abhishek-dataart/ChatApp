using ChatApp.Domain.Services.Rooms;
using Microsoft.AspNetCore.Mvc;

namespace ChatApp.Api.Controllers.Rooms;

internal static class RoomsErrorMapper
{
    internal static IActionResult FromError(ControllerBase ctrl, string code, string? message) => code switch
    {
        RoomsErrors.RoomNameTaken => ctrl.Problem(statusCode: 409, title: message ?? "Room name already taken.", extensions: Ext(code)),
        RoomsErrors.RoomNotFound => ctrl.Problem(statusCode: 404, title: message ?? "Room not found.", extensions: Ext(code)),
        RoomsErrors.RoomIsPrivate => ctrl.Problem(statusCode: 403, title: message ?? "Room is private.", extensions: Ext(code)),
        RoomsErrors.RoomFull => ctrl.Problem(statusCode: 409, title: message ?? "Room is at capacity.", extensions: Ext(code)),
        RoomsErrors.NotAMember => ctrl.Problem(statusCode: 403, title: message ?? "Not a member.", extensions: Ext(code)),
        RoomsErrors.AlreadyMember => ctrl.Problem(statusCode: 409, title: message ?? "Already a member.", extensions: Ext(code)),
        RoomsErrors.OwnerCannotLeave => ctrl.Problem(statusCode: 400, title: message ?? "Owner cannot leave.", extensions: Ext(code)),
        RoomsErrors.InvalidRoomName => ctrl.Problem(statusCode: 400, title: message ?? "Invalid room name.", extensions: Ext(code)),
        RoomsErrors.InvalidDescription => ctrl.Problem(statusCode: 400, title: message ?? "Invalid description.", extensions: Ext(code)),
        RoomsErrors.InvalidCapacity => ctrl.Problem(statusCode: 400, title: message ?? "Invalid capacity.", extensions: Ext(code)),
        RoomsErrors.SearchTooLong => ctrl.Problem(statusCode: 400, title: message ?? "Search query too long.", extensions: Ext(code)),
        RoomsErrors.InvitationNotFound => ctrl.Problem(statusCode: 404, title: message ?? "Invitation not found.", extensions: Ext(code)),
        RoomsErrors.InvitationExists => ctrl.Problem(statusCode: 409, title: message ?? "Invitation already exists.", extensions: Ext(code)),
        RoomsErrors.CannotInviteSelf => ctrl.Problem(statusCode: 400, title: message ?? "Cannot invite yourself.", extensions: Ext(code)),
        RoomsErrors.NotAdminOrOwner => ctrl.Problem(statusCode: 403, title: message ?? "Must be admin or owner.", extensions: Ext(code)),
        RoomsErrors.NoteTooLong => ctrl.Problem(statusCode: 400, title: message ?? "Note is too long.", extensions: Ext(code)),
        RoomsErrors.UserNotFound => ctrl.Problem(statusCode: 404, title: message ?? "User not found.", extensions: Ext(code)),
        RoomsErrors.RoomBanned => ctrl.Problem(statusCode: 403, title: message ?? "You are banned from this room.", extensions: Ext(code)),
        RoomsErrors.InviteeRoomBanned => ctrl.Problem(statusCode: 403, title: message ?? "Invitee is banned from this room.", extensions: Ext(code)),
        RoomsErrors.AlreadyBanned => ctrl.Problem(statusCode: 409, title: message ?? "User is already banned.", extensions: Ext(code)),
        RoomsErrors.BanNotFound => ctrl.Problem(statusCode: 404, title: message ?? "Active ban not found.", extensions: Ext(code)),
        RoomsErrors.CannotBanSelf => ctrl.Problem(statusCode: 400, title: message ?? "Cannot ban yourself.", extensions: Ext(code)),
        RoomsErrors.CannotBanOwner => ctrl.Problem(statusCode: 403, title: message ?? "Cannot ban the room owner.", extensions: Ext(code)),
        RoomsErrors.CannotBanPeerAdmin => ctrl.Problem(statusCode: 403, title: message ?? "Admins cannot ban other admins.", extensions: Ext(code)),
        RoomsErrors.CannotChangeOwnerRole => ctrl.Problem(statusCode: 400, title: message ?? "Cannot change the owner's role.", extensions: Ext(code)),
        RoomsErrors.CannotPromoteSelf => ctrl.Problem(statusCode: 400, title: message ?? "Cannot promote yourself.", extensions: Ext(code)),
        RoomsErrors.NotOwner => ctrl.Problem(statusCode: 403, title: message ?? "Only the owner can perform this action.", extensions: Ext(code)),
        RoomsErrors.CapacityBelowPopulation => ctrl.Problem(statusCode: 400, title: message ?? "Capacity cannot be below current member count.", extensions: Ext(code)),
        RoomsErrors.MemberNotFound => ctrl.Problem(statusCode: 404, title: message ?? "Member not found.", extensions: Ext(code)),
        RoomsErrors.InvalidRole => ctrl.Problem(statusCode: 400, title: message ?? "Invalid role.", extensions: Ext(code)),
        RoomsErrors.CannotKickSelf => ctrl.Problem(statusCode: 400, title: message ?? "Cannot remove yourself.", extensions: Ext(code)),
        RoomsErrors.CannotKickOwner => ctrl.Problem(statusCode: 403, title: message ?? "Cannot remove the room owner.", extensions: Ext(code)),
        RoomsErrors.CannotKickPeerAdmin => ctrl.Problem(statusCode: 403, title: message ?? "Admins cannot remove other admins.", extensions: Ext(code)),
        _ => ctrl.Problem(statusCode: 400, title: message ?? code, extensions: Ext(code)),
    };

    internal static Dictionary<string, object?> Ext(string code) => new() { ["code"] = code };
}
