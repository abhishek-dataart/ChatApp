namespace ChatApp.Domain.Services.Rooms;

public static class RoomsErrors
{
    public const string RoomNameTaken = "room_name_taken";
    public const string RoomNotFound = "room_not_found";
    public const string RoomIsPrivate = "room_is_private";
    public const string RoomFull = "room_full";
    public const string NotAMember = "not_a_member";
    public const string AlreadyMember = "already_member";
    public const string OwnerCannotLeave = "owner_cannot_leave";
    public const string InvalidRoomName = "invalid_room_name";
    public const string InvalidDescription = "invalid_description";
    public const string InvalidCapacity = "invalid_capacity";
    public const string SearchTooLong = "search_too_long";

    public const string UserNotFound = "user_not_found";
    public const string InvitationNotFound = "invitation_not_found";
    public const string InvitationExists = "invitation_exists";
    public const string CannotInviteSelf = "cannot_invite_self";
    public const string NotAdminOrOwner = "not_admin_or_owner";
    public const string NoteTooLong = "note_too_long";

    public const string RoomBanned = "room_banned";
    public const string InviteeRoomBanned = "invitee_room_banned";
    public const string AlreadyBanned = "already_banned";
    public const string BanNotFound = "ban_not_found";
    public const string CannotBanSelf = "cannot_ban_self";
    public const string CannotBanOwner = "cannot_ban_owner";
    public const string CannotBanPeerAdmin = "cannot_ban_peer_admin";
    public const string CannotChangeOwnerRole = "cannot_change_owner_role";
    public const string CannotPromoteSelf = "cannot_promote_self";
    public const string NotOwner = "not_owner";
    public const string CapacityBelowPopulation = "capacity_below_population";
    public const string MemberNotFound = "member_not_found";
    public const string InvalidRole = "invalid_role";
    public const string CannotKickSelf = "cannot_kick_self";
    public const string CannotKickOwner = "cannot_kick_owner";
    public const string CannotKickPeerAdmin = "cannot_kick_peer_admin";
}
