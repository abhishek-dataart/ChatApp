using ChatApp.Data.Entities.Identity;
using ChatApp.Data.Entities.Rooms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatApp.Data.Configurations.Rooms;

public class RoomMemberConfiguration : IEntityTypeConfiguration<RoomMember>
{
    public void Configure(EntityTypeBuilder<RoomMember> b)
    {
        b.ToTable("room_members");
        b.HasKey(m => new { m.RoomId, m.UserId });
        b.Property(m => m.Role).HasConversion<int>();
        b.HasIndex(m => m.UserId).HasDatabaseName("ix_room_members_user_id");
        b.HasOne<Room>().WithMany().HasForeignKey(m => m.RoomId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
