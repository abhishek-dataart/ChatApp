using ChatApp.Data.Entities.Social;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Data.Services.Social;

public class PersonalChatService(ChatDbContext db)
{
    public async Task<Guid> EnsureAsync(Guid a, Guid b, CancellationToken ct = default)
    {
        var low = a.CompareTo(b) < 0 ? a : b;
        var high = low == a ? b : a;

        var existing = await db.PersonalChats
            .AsNoTracking()
            .Where(p => p.UserAId == low && p.UserBId == high)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            return existing.Value;
        }

        var chat = new PersonalChat
        {
            Id = Guid.NewGuid(),
            UserAId = low,
            UserBId = high,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.PersonalChats.Add(chat);

        try
        {
            await db.SaveChangesAsync(ct);
            return chat.Id;
        }
        catch (DbUpdateException ex)
            when (ex.InnerException?.Message.Contains("23505") == true
               || ex.InnerException?.Message.Contains("ux_personal_chats_pair") == true)
        {
            db.Entry(chat).State = EntityState.Detached;
            return await db.PersonalChats
                .AsNoTracking()
                .Where(p => p.UserAId == low && p.UserBId == high)
                .Select(p => p.Id)
                .FirstAsync(ct);
        }
    }
}
