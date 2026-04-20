using ChatApp.Api.Contracts.Profile;
using ChatApp.Api.Infrastructure.Auth;
using ChatApp.Api.Infrastructure.Configuration;
using ChatApp.Data;
using ChatApp.Data.Services.Identity;
using ChatApp.Domain.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChatApp.Api.Controllers.Users;

[ApiController]
[Route("api/profile")]
[Authorize]
[EnableRateLimiting("general")]
public class ProfileController : ControllerBase
{
    private readonly ProfileService _profile;
    private readonly ICurrentUser _current;
    private readonly ChatDbContext _db;
    private readonly FilesOptions _files;
    private readonly IAccountDeletionService _accountDeletion;
    private readonly CookieWriter _cookieWriter;

    public ProfileController(
        ProfileService profile,
        ICurrentUser current,
        ChatDbContext db,
        IOptions<FilesOptions> files,
        IAccountDeletionService accountDeletion,
        CookieWriter cookieWriter)
    {
        _profile = profile;
        _current = current;
        _db = db;
        _files = files.Value;
        _accountDeletion = accountDeletion;
        _cookieWriter = cookieWriter;
    }

    [HttpPatch]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest body, CancellationToken ct)
    {
        var (ok, code, message) = await _profile.UpdateProfileAsync(_current.Id, body.DisplayName, body.SoundOnMessage, ct);
        if (!ok)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: message ?? code,
                extensions: new Dictionary<string, object?> { ["code"] = code });
        }

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == _current.Id, ct);
        if (user is null)
        {
            return Unauthorized();
        }

        return Ok(ToProfile(user));
    }

    [HttpPost("avatar")]
    [RequestSizeLimit(1_048_576)]
    [RequestFormLimits(MultipartBodyLengthLimit = 1_048_576)]
    public async Task<IActionResult> UploadAvatar(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "No file provided.",
                extensions: new Dictionary<string, object?> { ["code"] = "no_file" });
        }

        await using var stream = file.OpenReadStream();
        var (ok, code, message) = await _profile.SetAvatarAsync(_current.Id, stream, _files.Root, ct);
        if (!ok)
        {
            return Problem(
                statusCode: code == "unsupported_media_type" ? StatusCodes.Status415UnsupportedMediaType : StatusCodes.Status400BadRequest,
                title: message ?? code,
                extensions: new Dictionary<string, object?> { ["code"] = code });
        }

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == _current.Id, ct);
        if (user is null)
        {
            return Unauthorized();
        }

        return Ok(ToProfile(user));
    }

    [HttpDelete("avatar")]
    public async Task<IActionResult> DeleteAvatar(CancellationToken ct)
    {
        await _profile.ClearAvatarAsync(_current.Id, _files.Root, ct);
        return NoContent();
    }

    [HttpGet("avatar/{userId:guid}")]
    public async Task<IActionResult> GetAvatar(Guid userId, CancellationToken ct)
    {
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.DeletedAt == null)
            .Select(u => new { u.AvatarPath })
            .FirstOrDefaultAsync(ct);

        if (user?.AvatarPath is null)
        {
            return NotFound();
        }

        var filesRoot = Path.GetFullPath(_files.Root);
        var filePath = Path.GetFullPath(Path.Combine(filesRoot, user.AvatarPath));

        // Defence in depth: ensure resolved path stays under Files.Root
        if (!filePath.StartsWith(filesRoot, StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        if (!System.IO.File.Exists(filePath))
        {
            return NotFound();
        }

        var fileInfo = new FileInfo(filePath);
        var etag = $"\"{userId}:{fileInfo.LastWriteTimeUtc.Ticks}\"";

        if (Request.Headers.IfNoneMatch == etag)
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        Response.Headers.ETag = etag;
        Response.Headers.CacheControl = "private, max-age=300";
        Response.Headers.Append("X-Content-Type-Options", "nosniff");

        return PhysicalFile(filePath, "image/webp");
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountRequest body, CancellationToken ct)
    {
        var (ok, code, message) = await _accountDeletion.DeleteAccountAsync(_current.Id, body.Password, ct);
        if (!ok)
        {
            var statusCode = code == "invalid_current_password"
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status400BadRequest;
            return Problem(statusCode: statusCode, title: message ?? code,
                extensions: new Dictionary<string, object?> { ["code"] = code });
        }

        _cookieWriter.Clear(Response);
        return NoContent();
    }

    private static ProfileResponse ToProfile(ChatApp.Data.Entities.Identity.User u) => new(
        u.Id, u.Email, u.Username, u.DisplayName,
        u.AvatarPath is null ? null : $"/api/profile/avatar/{u.Id}",
        u.SoundOnMessage);
}
