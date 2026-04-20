using ChatApp.Data.Entities.Identity;
using ChatApp.Data.Entities.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatApp.Data.Configurations.Messaging;

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> b)
    {
        b.ToTable("messages");
        b.HasKey(m => m.Id);
        b.Property(m => m.Scope).HasConversion<int>();
        b.Property(m => m.Body).HasMaxLength(4000).IsRequired();
        b.HasIndex(m => new { m.PersonalChatId, m.CreatedAt, m.Id })
            .HasDatabaseName("ix_messages_personal_chat_created");
        b.HasIndex(m => new { m.RoomId, m.CreatedAt, m.Id })
            .HasDatabaseName("ix_messages_room_created");
        b.HasIndex(m => m.AuthorId).HasDatabaseName("ix_messages_author_id");
        b.HasOne<User>().WithMany().HasForeignKey(m => m.AuthorId).OnDelete(DeleteBehavior.SetNull);
    }
}
