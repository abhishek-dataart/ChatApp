using ChatApp.Domain.Services.Identity;
using FluentAssertions;
using Xunit;

namespace ChatApp.Tests.Unit;

public class SessionTokensTests
{
    [Fact]
    public void NewToken_is_base64url_and_unique()
    {
        var a = SessionTokens.NewToken();
        var b = SessionTokens.NewToken();

        a.Should().NotBe(b);
        a.Should().NotContain("+").And.NotContain("/").And.NotContain("=");
        // 32 bytes base64url-encoded without padding = 43 chars
        a.Length.Should().Be(43);
    }

    [Fact]
    public void Hash_is_sha256_of_decoded_token()
    {
        var token = SessionTokens.NewToken();
        var h1 = SessionTokens.Hash(token);
        var h2 = SessionTokens.Hash(token);

        h1.Length.Should().Be(32);
        h1.Should().BeEquivalentTo(h2);
    }

    [Fact]
    public void Different_tokens_produce_different_hashes()
    {
        var a = SessionTokens.Hash(SessionTokens.NewToken());
        var b = SessionTokens.Hash(SessionTokens.NewToken());
        a.Should().NotBeEquivalentTo(b);
    }
}
