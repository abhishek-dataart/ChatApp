using System.Collections.Concurrent;

namespace ChatApp.Domain.Services.Identity;

public class LoginRateLimiter
{
    private const int IpLimit = 10;
    private const int EmailLimit = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, Bucket> _ipBuckets = new();
    private readonly ConcurrentDictionary<string, Bucket> _emailBuckets = new();

    public bool TryAcquire(string ip, string emailNormalized)
    {
        var now = DateTimeOffset.UtcNow;
        if (!Check(_ipBuckets, ip, IpLimit, now))
        {
            return false;
        }
        if (!string.IsNullOrEmpty(emailNormalized) && !Check(_emailBuckets, emailNormalized, EmailLimit, now))
        {
            return false;
        }
        return true;
    }

    private static bool Check(ConcurrentDictionary<string, Bucket> map, string key, int limit, DateTimeOffset now)
    {
        var b = map.GetOrAdd(key, _ => new Bucket());
        lock (b)
        {
            if (now - b.WindowStart > Window)
            {
                b.WindowStart = now;
                b.Count = 0;
            }
            if (b.Count >= limit)
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
