namespace ChatApp.Data.Entities.Rooms;

public sealed class ModerationAudit
{
    public Guid Id { get; set; }
    public Guid RoomId { get; set; }
    public Guid? ActorId { get; set; }
    public Guid? TargetId { get; set; }
    public string Action { get; set; } = default!;
    public string? Detail { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
