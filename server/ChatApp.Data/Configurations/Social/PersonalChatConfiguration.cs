using ChatApp.Data.Entities.Social;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChatApp.Data.Configurations.Social;

public class PersonalChatConfiguration : IEntityTypeConfiguration<PersonalChat>
{
    public void Configure(EntityTypeBuilder<PersonalChat> b)
    {
        b.ToTable("personal_chats");
        b.HasKey(p => p.Id);
        b.HasIndex(p => new { p.UserAId, p.UserBId }).IsUnique().HasDatabaseName("ux_personal_chats_pair");
    }
}
