namespace ChatApp.Data.Entities.Messaging;

public class Message
{
    public Guid Id { get; set; }
    public MessageScope Scope { get; set; }
    public Guid? PersonalChatId { get; set; }
    public Guid? RoomId { get; set; }
    public Guid? AuthorId { get; set; }
    public string Body { get; set; } = default!;
    public Guid? ReplyToId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? EditedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
