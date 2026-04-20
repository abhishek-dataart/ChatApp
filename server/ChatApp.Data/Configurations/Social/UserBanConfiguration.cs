using ChatApp.Data.Entities.Identity;
using ChatApp.Data.Entities.Social;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatApp.Data.Configurations.Social;

public class UserBanConfiguration : IEntityTypeConfiguration<UserBan>
{
    public void Configure(EntityTypeBuilder<UserBan> b)
    {
        b.ToTable("user_bans");
        b.HasKey(ub => ub.Id);

        b.HasIndex(ub => new { ub.BannerId, ub.BannedId })
            .IsUnique()
            .HasFilter("\"lifted_at\" IS NULL")
            .HasDatabaseName("ux_user_bans_banner_banned_active");

        b.HasIndex(ub => ub.BannerId)
            .HasDatabaseName("ix_user_bans_banner_id");

        b.HasOne<User>().WithMany().HasForeignKey(ub => ub.BannerId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(ub => ub.BannedId).OnDelete(DeleteBehavior.Cascade);
    }
}
