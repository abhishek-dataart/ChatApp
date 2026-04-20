using ChatApp.Data.Entities.Identity;
using ChatApp.Data.Entities.Rooms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatApp.Data.Configurations.Rooms;

public class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> b)
    {
        b.ToTable("rooms");
        b.HasKey(r => r.Id);
        b.Property(r => r.Name).HasMaxLength(40).IsRequired();
        b.Property(r => r.NameNormalized).HasMaxLength(40).IsRequired();
        b.Property(r => r.Description).HasMaxLength(200).IsRequired();
        b.Property(r => r.Visibility).HasConversion<int>();
        b.Property(r => r.LogoPath).HasMaxLength(255).HasColumnName("room_logo_path");
        b.HasIndex(r => r.NameNormalized).HasDatabaseName("ux_rooms_name_normalized").IsUnique();
        b.HasIndex(r => r.OwnerId).HasDatabaseName("ix_rooms_owner_id");
        b.HasIndex(r => r.Visibility).HasDatabaseName("ix_rooms_visibility");
        b.HasOne<User>().WithMany().HasForeignKey(r => r.OwnerId).OnDelete(DeleteBehavior.Restrict);
    }
}
