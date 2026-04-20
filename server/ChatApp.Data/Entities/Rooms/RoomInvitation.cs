namespace ChatApp.Data.Entities.Rooms;

public sealed class RoomInvitation
{
    public Guid Id { get; set; }
    public Guid RoomId { get; set; }
    public Guid InviterId { get; set; }
    public Guid InviteeId { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
