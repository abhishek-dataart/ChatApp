using ChatApp.Domain.Entities;

namespace ChatApp.Data.Entities.Messaging;

public class Attachment
{
    public Guid Id { get; set; }
    public Guid? MessageId { get; set; }
    public Guid? UploaderId { get; set; }
    public AttachmentKind Kind { get; set; }
    public string OriginalFilename { get; set; } = string.Empty;
    public string StoredPath { get; set; } = string.Empty;
    public string Mime { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? ThumbPath { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScannedAt { get; set; }

    public Message? Message { get; set; }
}
