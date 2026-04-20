namespace ChatApp.Data.Entities.Rooms;

public sealed class RoomBan
{
    public Guid Id { get; set; }
    public Guid RoomId { get; set; }
    public Guid UserId { get; set; }
    public Guid BannedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LiftedAt { get; set; }
}
