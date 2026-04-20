using System.Collections.Concurrent;

namespace ChatApp.Domain.Services.Identity;

// Dedicated limiter for password-reset requests so IP-rotation attackers can't
// flood a single mailbox with reset emails within the shared login limiter's
// 10/min/IP budget. Caps reset requests at 3 per hour per normalized email.
public class PasswordResetRateLimiter
{
    private const int EmailLimit = 3;
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, Bucket> _emailBuckets = new();

    public bool TryAcquire(string emailNormalized)
    {
        if (string.IsNullOrEmpty(emailNormalized))
        {
            return true;
        }

        var now = DateTimeOffset.UtcNow;
        var b = _emailBuckets.GetOrAdd(emailNormalized, _ => new Bucket());
        lock (b)
        {
            if (now - b.WindowStart > Window)
            {
                b.WindowStart = now;
                b.Count = 0;
            }
            if (b.Count >= EmailLimit)
            {
                return false;
            }
            b.Count++;
            return true;
        }
    }

    private sealed class Bucket
    {
        public DateTimeOffset WindowStart { get; set; } = DateTimeOffset.UtcNow;
        public int Count { get; set; }
    }
}
