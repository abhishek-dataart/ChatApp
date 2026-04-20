using ChatApp.Data.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatApp.Data.Configurations.Identity;

public class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("sessions");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.UserId).IsRequired();
        builder.Property(s => s.CookieHash).IsRequired().HasMaxLength(32);
        builder.Property(s => s.UserAgent).IsRequired().HasMaxLength(512);
        builder.Property(s => s.Ip).IsRequired().HasMaxLength(64);
        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.LastSeenAt).IsRequired();

        builder.HasIndex(s => s.CookieHash).IsUnique();
        builder.HasIndex(s => new { s.UserId, s.RevokedAt });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
