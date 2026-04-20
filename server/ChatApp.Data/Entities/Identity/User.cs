namespace ChatApp.Data.Entities.Identity;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = default!;
    public string EmailNormalized { get; set; } = default!;
    public string Username { get; set; } = default!;
    public string UsernameNormalized { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string? AvatarPath { get; set; }
    public string PasswordHash { get; set; } = default!;
    public bool SoundOnMessage { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
