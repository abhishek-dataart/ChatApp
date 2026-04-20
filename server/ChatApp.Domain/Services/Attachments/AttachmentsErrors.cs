namespace ChatApp.Domain.Services.Attachments;

public static class AttachmentsErrors
{
    public const string FileRequired = "attachment.file_required";
    public const string UnsupportedKind = "attachment.unsupported_kind";
    public const string SizeExceeded = "attachment.size_exceeded";
    public const string MimeMismatch = "attachment.mime_mismatch";
    public const string ScanFailed = "attachment.scan_failed";
    public const string ScannerRejected = "attachment.scanner_rejected";
    public const string AttachmentNotFound = "attachment.not_found";
    public const string AttachmentAlreadyLinked = "attachment.already_linked";
    public const string AttachmentExpired = "attachment.expired";
    public const string NotUploader = "attachment.not_uploader";
    public const string NotAuthorized = "attachment.not_authorized";
    public const string TooManyAttachments = "attachment.too_many";
    public const string CommentTooLong = "attachment.comment_too_long";
}
