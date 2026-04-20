using ChatApp.Data.Entities.Identity;
using ChatApp.Data.Entities.Rooms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatApp.Data.Configurations.Rooms;

public class RoomBanConfiguration : IEntityTypeConfiguration<RoomBan>
{
    public void Configure(EntityTypeBuilder<RoomBan> b)
    {
        b.ToTable("room_bans");
        b.HasKey(rb => rb.Id);

        b.HasIndex(rb => new { rb.RoomId, rb.UserId })
            .IsUnique()
            .HasFilter("\"lifted_at\" IS NULL")
            .HasDatabaseName("ux_room_bans_room_user_active");

        b.HasIndex(rb => rb.RoomId)
            .HasDatabaseName("ix_room_bans_room_id");

        b.HasOne<Room>().WithMany().HasForeignKey(rb => rb.RoomId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(rb => rb.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(rb => rb.BannedById).OnDelete(DeleteBehavior.Cascade);
    }
}
