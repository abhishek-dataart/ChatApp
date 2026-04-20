using Microsoft.Extensions.Options;

namespace ChatApp.Api.Infrastructure.Auth;

public class CookieOptionsConfig
{
    public string Name { get; set; } = "chatapp_session";
    public string SameSite { get; set; } = "Lax";
    public bool Secure { get; set; } = true;
}

public class CookieWriter
{
    private readonly CookieOptionsConfig _opts;

    public CookieWriter(IOptions<CookieOptionsConfig> opts) => _opts = opts.Value;

    public string CookieName => _opts.Name;
    public bool IsSecure => _opts.Secure;

    public void Write(HttpResponse response, string token)
    {
        response.Cookies.Append(_opts.Name, token, BuildOptions(expireImmediately: false));
    }

    public void Clear(HttpResponse response)
    {
        response.Cookies.Append(_opts.Name, string.Empty, BuildOptions(expireImmediately: true));
    }

    private CookieOptions BuildOptions(bool expireImmediately)
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = _opts.Secure,
            SameSite = ParseSameSite(_opts.SameSite),
            Path = "/",
            Expires = expireImmediately ? DateTimeOffset.UnixEpoch : null,
            MaxAge = expireImmediately ? TimeSpan.Zero : null
        };
    }

    private static SameSiteMode ParseSameSite(string value) => value switch
    {
        "Strict" => SameSiteMode.Strict,
        "None" => SameSiteMode.None,
        _ => SameSiteMode.Lax
    };
}
