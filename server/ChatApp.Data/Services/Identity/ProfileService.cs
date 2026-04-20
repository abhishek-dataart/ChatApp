using ChatApp.Domain.Abstractions;
using ChatApp.Domain.Services.Identity;
using ChatApp.Domain.Services.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChatApp.Data.Services.Identity;

public class ProfileService
{
    private readonly ChatDbContext _db;
    private readonly IAvatarImageProcessor _imageProcessor;
    private readonly ILogger<ProfileService> _log;

    public ProfileService(ChatDbContext db, IAvatarImageProcessor imageProcessor, ILogger<ProfileService> log)
    {
        _db = db;
        _imageProcessor = imageProcessor;
        _log = log;
    }

    public async Task<(bool Ok, string? ErrorCode, string? ErrorMessage)> UpdateProfileAsync(
        Guid userId, string? displayName, bool? soundOnMessage, CancellationToken ct)
    {
        if (displayName is null && soundOnMessage is null)
        {
            return (false, "empty_request", "At least one field must be supplied.");
        }

        if (displayName is not null)
        {
            displayName = displayName.Trim();
            if (!AuthValidator.IsValidDisplayName(displayName))
            {
                return (false, "invalid_display_name", "Display name must be 1–64 characters.");
            }
        }

        await _db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.DisplayName, u => displayName != null ? displayName : u.DisplayName)
                .SetProperty(u => u.SoundOnMessage, u => soundOnMessage != null ? soundOnMessage.Value : u.SoundOnMessage),
            ct);

        return (true, null, null);
    }

    public async Task<(bool Ok, string? ErrorCode, string? ErrorMessage)> SetAvatarAsync(
        Guid userId, Stream stream, string filesRoot, CancellationToken ct)
    {
        if (!await ImageMagicBytes.IsSupportedImageAsync(stream, ct))
        {
            return (false, "unsupported_media_type", "File type not supported.");
        }

        var avatarsDir = Path.Combine(filesRoot, "avatars");
        Directory.CreateDirectory(avatarsDir);

        var tmpPath = Path.Combine(avatarsDir, $"{userId}.webp.tmp");
        var finalPath = Path.Combine(avatarsDir, $"{userId}.webp");

        try
        {
            await using (var tmp = File.Create(tmpPath))
            {
                await _imageProcessor.EncodeAsync(stream, tmp, ct);
            }
            File.Move(tmpPath, finalPath, overwrite: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Avatar encode failed for user {UserId}", userId);
            try { File.Delete(tmpPath); } catch { /* best effort */ }
            return (false, "invalid_image", "Image could not be processed.");
        }

        var relativePath = $"avatars/{userId}.webp";
        await _db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.AvatarPath, relativePath), ct);

        return (true, null, null);
    }

    public async Task ClearAvatarAsync(Guid userId, string filesRoot, CancellationToken ct)
    {
        await _db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.AvatarPath, (string?)null), ct);

        var filePath = Path.Combine(filesRoot, "avatars", $"{userId}.webp");
        try
        {
            File.Delete(filePath);
        }
        catch (FileNotFoundException)
        {
            // already gone
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Best-effort avatar file delete failed for user {UserId}", userId);
        }
    }

}
