namespace ChatApp.Api.Contracts.Auth;

public record RegisterRequest(string Email, string Username, string DisplayName, string Password);

public record LoginRequest(string Email, string Password, bool KeepSignedIn = false);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Token, string NewPassword);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record MeResponse(Guid Id, string Email, string Username, string DisplayName, string? AvatarUrl, bool SoundOnMessage, Guid CurrentSessionId);
