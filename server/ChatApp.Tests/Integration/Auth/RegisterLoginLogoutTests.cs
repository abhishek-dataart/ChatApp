using System.Net;
using System.Net.Http.Json;
using ChatApp.Api.Contracts.Auth;
using ChatApp.Tests.Integration.Infrastructure;
using FluentAssertions;
using Xunit;

namespace ChatApp.Tests.Integration.Auth;

[Collection(PostgresCollection.Name)]
public class RegisterLoginLogoutTests(PostgresFixture pg) : IAsyncLifetime
{
    private ChatAppFactory _factory = default!;

    public async Task InitializeAsync()
    {
        _factory = new ChatAppFactory(pg.ConnectionString);
        await DbSeeder.ResetAsync(_factory, pg.ConnectionString);
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Register_sets_session_and_csrf_cookies()
    {
        var http = _factory.CreateClient();
        var body = new RegisterRequest("ada@example.com", "ada_l", "Ada Lovelace", "password1a");
        var resp = await http.PostAsJsonAsync("/api/auth/register", body);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        resp.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        var all = string.Join('\n', cookies!);
        all.Should().Contain("chatapp_session=");
        all.Should().Contain("csrf_token=");
    }

    [Fact]
    public async Task Login_after_register_succeeds()
    {
        var http = _factory.CreateClient();
        await http.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("login@example.com", "login_u", "Login User", "password1a"));

        var fresh = _factory.CreateClient();
        var resp = await fresh.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("login@example.com", "password1a"));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var me = await resp.Content.ReadFromJsonAsync<MeResponse>();
        me!.Email.Should().Be("login@example.com");
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401()
    {
        var http = _factory.CreateClient();
        await http.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("wrong@example.com", "wrong_u", "Wrong User", "password1a"));

        var fresh = _factory.CreateClient();
        var resp = await fresh.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("wrong@example.com", "bad-password"));

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_returns_current_user()
    {
        var session = await AuthenticatedClient.RegisterAndLoginAsync(_factory);
        var resp = await session.Http.GetAsync("/api/auth/me");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var me = await resp.Content.ReadFromJsonAsync<MeResponse>();
        me!.Id.Should().Be(session.UserId);
    }

    [Fact]
    public async Task Logout_clears_session_and_me_requires_reauth()
    {
        var session = await AuthenticatedClient.RegisterAndLoginAsync(_factory);
        var logout = await session.Http.PostAsync("/api/auth/logout", content: null);
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var me = await session.Http.GetAsync("/api/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
