namespace ChatApp.Domain.Services.Attachments;

public sealed class AttachmentsOptions
{
    public string FilesRoot { get; set; } = string.Empty;
    public long MaxImageBytes { get; set; } = 3_145_728;
    public long MaxFileBytes { get; set; } = 20_971_520;
    public int MaxPerMessage { get; set; } = 10;
    public int UnlinkedTtlMinutes { get; set; } = 60;
    public int PurgeIntervalMinutes { get; set; } = 10;
}
