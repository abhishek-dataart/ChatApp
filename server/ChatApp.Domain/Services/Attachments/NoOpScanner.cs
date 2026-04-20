using ChatApp.Domain.Abstractions;

namespace ChatApp.Domain.Services.Attachments;

public sealed class NoOpScanner : IAttachmentScanner
{
    public Task<AttachmentScanResult> ScanAsync(Stream content, string claimedMime, CancellationToken ct) =>
        Task.FromResult<AttachmentScanResult>(new AttachmentScanResult.Clean());
}
