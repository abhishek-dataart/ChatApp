namespace ChatApp.Domain.Abstractions;

public abstract record AttachmentScanResult
{
    public sealed record Clean : AttachmentScanResult;
    public sealed record Infected(string Reason) : AttachmentScanResult;
}

public interface IAttachmentScanner
{
    Task<AttachmentScanResult> ScanAsync(Stream content, string claimedMime, CancellationToken ct);
}
