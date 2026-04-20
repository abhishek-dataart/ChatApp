using ChatApp.Api.Contracts.Attachments;
using ChatApp.Data.Services.Attachments;
using ChatApp.Domain.Abstractions;
using ChatApp.Domain.Services.Attachments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChatApp.Api.Controllers.Attachments;

[ApiController]
[Route("api/attachments")]
[Authorize]
public sealed class AttachmentsController(
    AttachmentService attachments,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(20 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 20 * 1024 * 1024)]
    [EnableRateLimiting("uploads")]
    public async Task<IActionResult> Upload(
        [FromForm] IFormFile? file,
        [FromForm] string? kind,
        [FromForm] string? comment,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "A file is required.", extensions: Ext(AttachmentsErrors.FileRequired));
        }

        if (string.IsNullOrWhiteSpace(kind))
        {
            return Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "The kind field is required (image|file).", extensions: Ext(AttachmentsErrors.UnsupportedKind));
        }

        await using var stream = file.OpenReadStream();
        var (ok, code, message, value) = await attachments.UploadAsync(
            currentUser.Id, kind, file.ContentType, file.FileName, file.Length, comment, stream, ct);

        if (!ok)
        {
            return code switch
            {
                AttachmentsErrors.UnsupportedKind => UnprocessableEntity(code, message),
                AttachmentsErrors.SizeExceeded => UnprocessableEntity(code, message),
                AttachmentsErrors.MimeMismatch => UnprocessableEntity(code, message),
                AttachmentsErrors.CommentTooLong => UnprocessableEntity(code, message),
                AttachmentsErrors.ScannerRejected => UnprocessableEntity(code, message),
                _ => Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Upload failed.", extensions: Ext(code)),
            };
        }

        var v = value!;
        var response = new UploadAttachmentResponse(
            v.Id,
            v.Kind.ToString().ToLowerInvariant(),
            v.OriginalFilename,
            v.Mime,
            v.SizeBytes,
            v.Comment,
            v.HasThumb ? $"/api/attachments/{v.Id}/thumb" : null,
            $"/api/attachments/{v.Id}",
            v.CreatedAt);

        return Ok(response);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var (ok, code, value) = await attachments.OpenOriginalAsync(currentUser.Id, id, ct);
        if (!ok)
        {
            return MapReadError(code!);
        }

        var (fileStream, mime, filename) = value!.Value;
        Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";
        return File(fileStream, mime);
    }

    [HttpGet("{id:guid}/thumb")]
    public async Task<IActionResult> Thumb(Guid id, CancellationToken ct)
    {
        var (ok, code, stream) = await attachments.OpenThumbAsync(currentUser.Id, id, ct);
        if (!ok)
        {
            return MapReadError(code!);
        }

        Response.Headers["Cache-Control"] = "private, max-age=3600";
        return File(stream!, "image/jpeg");
    }

    private IActionResult MapReadError(string code) => code switch
    {
        AttachmentsErrors.NotAuthorized => Problem(statusCode: StatusCodes.Status403Forbidden, title: "Access denied.", extensions: Ext(code)),
        _ => Problem(statusCode: StatusCodes.Status404NotFound, title: "Attachment not found.", extensions: Ext(code)),
    };

    private IActionResult UnprocessableEntity(string? code, string? title) =>
        Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: title, extensions: Ext(code));

    private static Dictionary<string, object?> Ext(string? code) => new() { ["code"] = code };
}
