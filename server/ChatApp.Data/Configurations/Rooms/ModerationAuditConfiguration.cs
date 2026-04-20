using ChatApp.Data.Entities.Identity;
using ChatApp.Data.Entities.Rooms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatApp.Data.Configurations.Rooms;

public class ModerationAuditConfiguration : IEntityTypeConfiguration<ModerationAudit>
{
    public void Configure(EntityTypeBuilder<ModerationAudit> b)
    {
        b.ToTable("moderation_audit");
        b.HasKey(a => a.Id);

        b.Property(a => a.Action).HasMaxLength(32);
        b.Property(a => a.Detail).HasColumnType("jsonb");

        b.HasIndex(a => new { a.RoomId, a.CreatedAt, a.Id })
            .HasDatabaseName("ix_moderation_audit_room_created");

        b.HasOne<Room>().WithMany().HasForeignKey(a => a.RoomId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(a => a.ActorId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<User>().WithMany().HasForeignKey(a => a.TargetId).OnDelete(DeleteBehavior.SetNull);
    }
}
