namespace ChatApp.Domain.Services.Social;

public static class SocialErrors
{
    public const string CannotFriendSelf = "cannot_friend_self";
    public const string UserNotFound = "user_not_found";
    public const string FriendshipExists = "friendship_exists";
    public const string FriendshipNotFound = "friendship_not_found";
    public const string NoteTooLong = "note_too_long";
    public const string UserBanned = "user_banned";
    public const string CannotBanSelf = "cannot_ban_self";
    public const string AlreadyBanned = "already_banned";
    public const string BanNotFound = "ban_not_found";
}
