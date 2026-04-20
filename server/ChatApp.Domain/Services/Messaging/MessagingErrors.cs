namespace ChatApp.Domain.Services.Messaging;

public static class MessagingErrors
{
    public const string ChatNotFound = "chat_not_found";
    public const string NotParticipant = "not_participant";
    public const string RoomNotFound = "room_not_found";
    public const string NotMember = "not_member";
    public const string BodyTooLong = "body_too_long";
    public const string BodyEmpty = "body_empty";
    public const string MessageNotFound = "message_not_found";
    public const string NotAuthor = "not_author";
    public const string NotAuthorized = "not_authorized";
    public const string MessageAlreadyDeleted = "message_already_deleted";
    public const string UserBanned = "user_banned";
}
