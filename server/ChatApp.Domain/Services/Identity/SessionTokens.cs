using System.Security.Cryptography;

namespace ChatApp.Domain.Services.Identity;

public static class SessionTokens
{
    public const int TokenBytes = 32;

    public static string NewToken()
    {
        Span<byte> buf = stackalloc byte[TokenBytes];
        RandomNumberGenerator.Fill(buf);
        return Base64UrlEncode(buf);
    }

    public static byte[] Hash(string token)
    {
        var raw = Base64UrlDecode(token);
        return SHA256.HashData(raw);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        var s = Convert.ToBase64String(bytes);
        return s.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
