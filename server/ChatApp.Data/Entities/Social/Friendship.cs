namespace ChatApp.Data.Entities.Social;

public class Friendship
{
    public Guid Id { get; set; }
    public Guid UserIdLow { get; set; }
    public Guid UserIdHigh { get; set; }
    public FriendshipState State { get; set; }
    public Guid RequesterId { get; set; }
    public string? RequestNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
}
