namespace ChatApp.Domain.Services.Attachments;

public sealed record AttachmentSummary(
    Guid Id,
    string Kind,
    string OriginalFilename,
    string Mime,
    long SizeBytes,
    string? Comment,
    string? ThumbUrl,
    string DownloadUrl,
    DateTimeOffset CreatedAt);
