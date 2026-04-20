using ChatApp.Data.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatApp.Data.Configurations.Identity;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email).IsRequired().HasMaxLength(256);
        builder.Property(u => u.EmailNormalized).IsRequired().HasMaxLength(256);
        builder.Property(u => u.Username).IsRequired().HasMaxLength(32);
        builder.Property(u => u.UsernameNormalized).IsRequired().HasMaxLength(32);
        builder.Property(u => u.DisplayName).IsRequired().HasMaxLength(64);
        builder.Property(u => u.AvatarPath).HasMaxLength(512);
        builder.Property(u => u.SoundOnMessage).IsRequired().HasDefaultValue(true);
        builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(512);
        builder.Property(u => u.CreatedAt).IsRequired();

        builder.HasIndex(u => u.EmailNormalized).IsUnique();
        builder.HasIndex(u => u.UsernameNormalized).IsUnique();
    }
}
