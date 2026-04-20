using ChatApp.Domain.Services.Identity;
using FluentAssertions;
using Xunit;

namespace ChatApp.Tests.Unit;

public class AuthValidatorTests
{
    [Theory]
    [InlineData("abc", true)]
    [InlineData("ab", false)]
    [InlineData("user_1", true)]
    [InlineData("User1", false)]      // uppercase rejected
    [InlineData("has space", false)]
    [InlineData("", false)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaa", false)] // 21 chars
    public void IsValidUsername_checks_pattern(string input, bool expected)
        => AuthValidator.IsValidUsername(input).Should().Be(expected);

    [Theory]
    [InlineData("a@b.co", true)]
    [InlineData("abc@example.com", true)]
    [InlineData("no-at-sign", false)]
    [InlineData("two@@at.com", false)]
    [InlineData("no@dot", false)]
    [InlineData("", false)]
    public void IsValidEmail_checks_shape(string input, bool expected)
        => AuthValidator.IsValidEmail(input).Should().Be(expected);

    [Theory]
    [InlineData("Ada", true)]
    [InlineData("  Ada  ", true)]
    [InlineData("   ", false)]
    [InlineData("", false)]
    public void IsValidDisplayName_rejects_blank(string input, bool expected)
        => AuthValidator.IsValidDisplayName(input).Should().Be(expected);

    [Theory]
    [InlineData("password1a", true)]       // 10 chars, letter+digit
    [InlineData("password", false)]        // no digit
    [InlineData("1234567890", false)]      // no letter
    [InlineData("pass1", false)]           // too short
    [InlineData("", false)]
    public void IsValidPassword_requires_mix(string input, bool expected)
        => AuthValidator.IsValidPassword(input).Should().Be(expected);

    [Fact]
    public void NormalizeEmail_trims_and_lowercases()
        => AuthValidator.NormalizeEmail("  Foo@Example.COM ").Should().Be("foo@example.com");

    [Fact]
    public void NormalizeUsername_trims_and_lowercases()
        => AuthValidator.NormalizeUsername(" ADA ").Should().Be("ada");
}
