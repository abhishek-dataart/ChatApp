namespace ChatApp.Api.Contracts.Attachments;

public sealed record UploadAttachmentResponse(
    Guid Id,
    string Kind,
    string OriginalFilename,
    string Mime,
    long SizeBytes,
    string? Comment,
    string? ThumbUrl,
    string DownloadUrl,
    DateTimeOffset CreatedAt);
