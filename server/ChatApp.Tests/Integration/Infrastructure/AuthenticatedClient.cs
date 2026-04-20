using System.Net.Http.Json;
using ChatApp.Api.Contracts.Auth;

namespace ChatApp.Tests.Integration.Infrastructure;

public static class AuthenticatedClient
{
    public sealed record Session(HttpClient Http, Guid UserId, string CsrfToken);

    public static async Task<Session> RegisterAndLoginAsync(
        ChatAppFactory factory,
        string? email = null,
        string? username = null,
        string password = "password1a")
    {
        email ??= $"u{Guid.NewGuid():N}@example.com";
        username ??= $"u{Guid.NewGuid():N}".Substring(0, 16);

        var http = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });

        var body = new RegisterRequest(email, username, "Test User", password);
        var response = await http.PostAsJsonAsync("/api/auth/register", body);
        response.EnsureSuccessStatusCode();
        var me = await response.Content.ReadFromJsonAsync<MeResponse>()
            ?? throw new InvalidOperationException("register returned no body");

        // Extract csrf_token from Set-Cookie so subsequent non-GET requests pass the double-submit check.
        var csrf = ExtractCsrf(response);
        http.DefaultRequestHeaders.Add("X-Csrf-Token", csrf);

        return new Session(http, me.Id, csrf);
    }

    private static string ExtractCsrf(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            return string.Empty;
        }

        foreach (var h in setCookies)
        {
            const string prefix = "csrf_token=";
            var idx = h.IndexOf(prefix, StringComparison.Ordinal);
            if (idx < 0) continue;
            var start = idx + prefix.Length;
            var end = h.IndexOf(';', start);
            return end < 0 ? h[start..] : h[start..end];
        }
        return string.Empty;
    }
}
