namespace ChatApp.Api.Contracts.Auth;

public record RegisterRequest(string Email, string Username, string DisplayName, string Password);

public record LoginRequest(string Email, string Password);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record MeResponse(Guid Id, string Email, string Username, string DisplayName, string? AvatarUrl, bool SoundOnMessage, Guid CurrentSessionId);
