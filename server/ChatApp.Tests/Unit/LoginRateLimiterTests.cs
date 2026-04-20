using ChatApp.Domain.Services.Identity;
using FluentAssertions;
using Xunit;

namespace ChatApp.Tests.Unit;

public class LoginRateLimiterTests
{
    [Fact]
    public void Allows_up_to_email_limit_then_rejects()
    {
        var rl = new LoginRateLimiter();
        var ip = "1.2.3.4";
        var email = "a@b.co";

        for (var i = 0; i < 5; i++)
        {
            rl.TryAcquire(ip, email).Should().BeTrue($"attempt {i + 1} within email limit");
        }

        rl.TryAcquire(ip, email).Should().BeFalse("email limit is 5/min");
    }

    [Fact]
    public void Ip_limit_10_trips_even_across_emails()
    {
        var rl = new LoginRateLimiter();
        var ip = "9.9.9.9";

        for (var i = 0; i < 10; i++)
        {
            // different emails each time so email bucket never trips
            rl.TryAcquire(ip, $"user{i}@b.co").Should().BeTrue();
        }

        rl.TryAcquire(ip, "user99@b.co").Should().BeFalse();
    }

    [Fact]
    public void Empty_email_skips_email_bucket()
    {
        var rl = new LoginRateLimiter();

        // Ip limit 10; should allow 10 with empty email.
        for (var i = 0; i < 10; i++)
        {
            rl.TryAcquire($"10.0.0.{i}", string.Empty).Should().BeTrue();
        }
    }
}
