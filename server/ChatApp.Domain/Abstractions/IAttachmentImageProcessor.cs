namespace ChatApp.Domain.Abstractions;

public interface IAttachmentImageProcessor
{
    Task<byte[]> CreateThumbAsync(Stream source, CancellationToken ct);

    Task<byte[]> SanitizeAsync(Stream source, string mime, CancellationToken ct);
}
