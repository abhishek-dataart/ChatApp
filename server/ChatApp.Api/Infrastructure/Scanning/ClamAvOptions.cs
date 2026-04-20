namespace ChatApp.Api.Infrastructure.Scanning;

public sealed class ClamAvOptions
{
    public string Host { get; set; } = "clamav";
    public int Port { get; set; } = 3310;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public long MaxStreamBytes { get; set; } = 25 * 1024 * 1024;
}
