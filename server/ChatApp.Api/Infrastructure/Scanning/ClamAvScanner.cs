using ChatApp.Domain.Abstractions;
using Microsoft.Extensions.Options;
using nClam;

namespace ChatApp.Api.Infrastructure.Scanning;

public sealed class ClamAvScanner(IOptions<ClamAvOptions> opts, ILogger<ClamAvScanner> log)
    : IAttachmentScanner
{
    private readonly ClamAvOptions _opts = opts.Value;

    public async Task<AttachmentScanResult> ScanAsync(Stream content, string claimedMime, CancellationToken ct)
    {
        var client = new ClamClient(_opts.Host, _opts.Port)
        {
            MaxStreamSize = _opts.MaxStreamBytes,
        };

        ClamScanResult result;
        try
        {
            result = await client.SendAndScanFileAsync(content, ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "ClamAV scan failed");
            return new AttachmentScanResult.Infected("Scanner unavailable");
        }

        return result.Result switch
        {
            ClamScanResults.Clean => new AttachmentScanResult.Clean(),
            ClamScanResults.VirusDetected => new AttachmentScanResult.Infected(
                result.InfectedFiles?.FirstOrDefault()?.VirusName ?? "Infected"),
            ClamScanResults.Error => new AttachmentScanResult.Infected("Scanner error"),
            _ => new AttachmentScanResult.Infected("Unknown"),
        };
    }
}
