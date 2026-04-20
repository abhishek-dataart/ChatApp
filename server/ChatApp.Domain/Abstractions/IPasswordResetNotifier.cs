namespace ChatApp.Domain.Abstractions;

public interface IPasswordResetNotifier
{
    Task SendAsync(string email, string displayName, string token, CancellationToken ct);
}
