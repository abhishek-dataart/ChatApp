using System.Text.RegularExpressions;

namespace ChatApp.Domain.Services.Identity;

public static partial class AuthValidator
{
    [GeneratedRegex("^[a-z0-9_]{3,20}$")]
    private static partial Regex UsernameRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

    public static bool IsValidUsername(string username) =>
        !string.IsNullOrEmpty(username) && UsernameRegex().IsMatch(username);

    public static bool IsValidEmail(string email) =>
        !string.IsNullOrEmpty(email) && email.Length <= 256 && EmailRegex().IsMatch(email);

    public static bool IsValidDisplayName(string displayName) =>
        !string.IsNullOrWhiteSpace(displayName) && displayName.Trim().Length >= 1 && displayName.Length <= 64;

    public static bool IsValidPassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 10)
        {
            return false;
        }
        var hasLetter = false;
        var hasDigit = false;
        foreach (var c in password)
        {
            if (char.IsLetter(c))
            {
                hasLetter = true;
            }
            else if (char.IsDigit(c))
            {
                hasDigit = true;
            }
            if (hasLetter && hasDigit)
            {
                return true;
            }
        }
        return false;
    }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
    public static string NormalizeUsername(string username) => username.Trim().ToLowerInvariant();
}
