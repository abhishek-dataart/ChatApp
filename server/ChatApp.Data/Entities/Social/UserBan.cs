namespace ChatApp.Data.Entities.Social;

public sealed class UserBan
{
    public Guid Id { get; set; }
    public Guid BannerId { get; set; }
    public Guid BannedId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LiftedAt { get; set; }
}
