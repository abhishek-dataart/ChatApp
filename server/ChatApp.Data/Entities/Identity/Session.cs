namespace ChatApp.Data.Entities.Identity;

public class Session
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public byte[] CookieHash { get; set; } = default!;
    public string UserAgent { get; set; } = default!;
    public string Ip { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
