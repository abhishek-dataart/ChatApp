namespace ChatApp.Data.Entities.Rooms;

public sealed class RoomMember
{
    public Guid RoomId { get; set; }
    public Guid UserId { get; set; }
    public RoomRole Role { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
}
