using ChatApp.Data.Entities.Identity;
using ChatApp.Data.Entities.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatApp.Data.Configurations.Messaging;

public class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> b)
    {
        b.ToTable("attachments");
        b.HasKey(a => a.Id);
        b.Property(a => a.Kind).HasConversion<int>();
        b.Property(a => a.OriginalFilename).HasMaxLength(512).IsRequired();
        b.Property(a => a.StoredPath).HasMaxLength(1024).IsRequired();
        b.Property(a => a.Mime).HasMaxLength(128).IsRequired();
        b.Property(a => a.Comment).HasMaxLength(500);
        b.HasIndex(a => a.MessageId).HasDatabaseName("ix_attachments_message_id");
        b.HasIndex(a => a.CreatedAt)
            .HasFilter("message_id IS NULL")
            .HasDatabaseName("ix_attachments_unlinked_created");
        b.HasIndex(a => a.UploaderId).HasDatabaseName("ix_attachments_uploader_id");
        b.HasOne(a => a.Message)
            .WithMany()
            .HasForeignKey(a => a.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(a => a.UploaderId).OnDelete(DeleteBehavior.SetNull);
    }
}
