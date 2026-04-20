namespace ChatApp.Data.Entities.Messaging;

public class UnreadMarker
{
    public Guid UserId { get; set; }
    public MessageScope Scope { get; set; }
    public Guid ScopeId { get; set; }
    public int UnreadCount { get; set; }
    public DateTimeOffset? LastReadAt { get; set; }
}
