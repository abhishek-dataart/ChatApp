using ChatApp.Data.Entities.Identity;
using ChatApp.Data.Entities.Social;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatApp.Data.Configurations.Social;

public class FriendshipConfiguration : IEntityTypeConfiguration<Friendship>
{
    public void Configure(EntityTypeBuilder<Friendship> b)
    {
        b.ToTable("friendships");
        b.HasKey(f => f.Id);
        b.Property(f => f.State).HasConversion<int>();
        b.Property(f => f.RequestNote).HasMaxLength(500);
        b.HasIndex(f => new { f.UserIdLow, f.UserIdHigh }).IsUnique().HasDatabaseName("ux_friendships_pair");
        b.HasIndex(f => f.RequesterId).HasDatabaseName("ix_friendships_requester_id");
        b.HasOne<User>().WithMany().HasForeignKey(f => f.UserIdLow).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(f => f.UserIdHigh).OnDelete(DeleteBehavior.Cascade);
    }
}
