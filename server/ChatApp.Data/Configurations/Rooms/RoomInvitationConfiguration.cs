using ChatApp.Data.Entities.Identity;
using ChatApp.Data.Entities.Rooms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatApp.Data.Configurations.Rooms;

public class RoomInvitationConfiguration : IEntityTypeConfiguration<RoomInvitation>
{
    public void Configure(EntityTypeBuilder<RoomInvitation> b)
    {
        b.ToTable("room_invitations");
        b.HasKey(i => i.Id);
        b.Property(i => i.Note).HasMaxLength(200);

        b.HasIndex(i => new { i.RoomId, i.InviteeId })
            .IsUnique()
            .HasDatabaseName("ux_room_invitations_room_invitee");

        b.HasIndex(i => i.InviteeId)
            .HasDatabaseName("ix_room_invitations_invitee_id");

        b.HasIndex(i => i.InviterId)
            .HasDatabaseName("ix_room_invitations_inviter_id");

        b.HasOne<Room>().WithMany().HasForeignKey(i => i.RoomId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(i => i.InviterId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(i => i.InviteeId).OnDelete(DeleteBehavior.Cascade);
    }
}
