namespace ChatApp.Data.Entities.Rooms;

public sealed class Room
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string NameNormalized { get; set; } = default!;
    public string Description { get; set; } = default!;
    public RoomVisibility Visibility { get; set; }
    public Guid OwnerId { get; set; }
    public int Capacity { get; set; } = 1000;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? LogoPath { get; set; }
}
