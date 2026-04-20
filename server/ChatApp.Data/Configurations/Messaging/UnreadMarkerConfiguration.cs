using ChatApp.Data.Entities.Identity;
using ChatApp.Data.Entities.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatApp.Data.Configurations.Messaging;

public class UnreadMarkerConfiguration : IEntityTypeConfiguration<UnreadMarker>
{
    public void Configure(EntityTypeBuilder<UnreadMarker> b)
    {
        b.ToTable("unread_markers");
        b.HasKey(u => new { u.UserId, u.Scope, u.ScopeId });
        b.Property(u => u.Scope).HasConversion<int>();
        b.HasIndex(u => u.UserId)
            .HasFilter("unread_count > 0")
            .HasDatabaseName("ix_unread_markers_user_unread");
        b.HasOne<User>().WithMany().HasForeignKey(u => u.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
