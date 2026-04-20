using ChatApp.Domain.Entities;

namespace ChatApp.Domain.Services.Attachments;

public sealed record AttachmentUploadResult(
    Guid Id,
    AttachmentKind Kind,
    string OriginalFilename,
    string Mime,
    long SizeBytes,
    string? Comment,
    bool HasThumb,
    DateTimeOffset CreatedAt);
