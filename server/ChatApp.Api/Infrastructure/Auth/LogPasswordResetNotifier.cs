using ChatApp.Domain.Abstractions;

namespace ChatApp.Api.Infrastructure.Auth;

// Default implementation: logs the reset URL. Swap for an SMTP/email provider in production.
public class LogPasswordResetNotifier : IPasswordResetNotifier
{
    private readonly ILogger<LogPasswordResetNotifier> _logger;

    public LogPasswordResetNotifier(ILogger<LogPasswordResetNotifier> logger) => _logger = logger;

    public Task SendAsync(string email, string displayName, string token, CancellationToken ct)
    {
        _logger.LogInformation(
            "Password reset requested for {Email} ({DisplayName}). Reset token: {Token}",
            email, displayName, token);
        return Task.CompletedTask;
    }
}
