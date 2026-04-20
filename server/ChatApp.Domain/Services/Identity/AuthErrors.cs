namespace ChatApp.Domain.Services.Identity;

public static class AuthErrors
{
    public const string EmailTaken = "email_taken";
    public const string UsernameTaken = "username_taken";
    public const string InvalidCredentials = "invalid_credentials";
    public const string InvalidCurrentPassword = "invalid_current_password";
    public const string ValidationFailed = "validation_failed";
    public const string InvalidResetToken = "invalid_reset_token";
}

public sealed record AuthResult<T>(T? Value, string? ErrorCode, string? ErrorMessage)
{
    public bool IsSuccess => ErrorCode is null;

    public static AuthResult<T> Success(T value) => new(value, null, null);
    public static AuthResult<T> Failure(string code, string? message = null) => new(default, code, message);
}
