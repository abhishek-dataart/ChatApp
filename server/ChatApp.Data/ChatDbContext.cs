using ChatApp.Data.Entities.Identity;
using ChatApp.Data.Entities.Messaging;
using ChatApp.Data.Entities.Rooms;
using ChatApp.Data.Entities.Social;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Data;

public class ChatDbContext : DbContext
{
    public ChatDbContext(DbContextOptions<ChatDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Session> Sessions => Set<Session>();

    // Social
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<PersonalChat> PersonalChats => Set<PersonalChat>();
    public DbSet<UserBan> UserBans => Set<UserBan>();

    // Rooms
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RoomMember> RoomMembers => Set<RoomMember>();
    public DbSet<RoomInvitation> RoomInvitations => Set<RoomInvitation>();
    public DbSet<RoomBan> RoomBans => Set<RoomBan>();
    public DbSet<ModerationAudit> ModerationAudits => Set<ModerationAudit>();

    // Messaging
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<UnreadMarker> UnreadMarkers => Set<UnreadMarker>();

    // Attachments
    public DbSet<Attachment> Attachments => Set<Attachment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ChatDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
