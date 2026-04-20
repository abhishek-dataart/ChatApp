using ChatApp.Api.Contracts.Rooms;
using ChatApp.Api.Infrastructure.Configuration;
using ChatApp.Data.Services.Rooms;
using ChatApp.Domain.Abstractions;
using ChatApp.Domain.Services.Rooms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ChatApp.Api.Controllers.Rooms;

[ApiController]
[Route("api/rooms")]
[Authorize]
public class RoomsController(RoomService rooms, ICurrentUser current, IOptions<FilesOptions> files) : ControllerBase
{
    private readonly FilesOptions _files = files.Value;
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRoomRequest body, CancellationToken ct)
    {
        var (ok, code, message, value) = await rooms.CreateAsync(
            current.Id, body.Name, body.Description, body.Visibility, body.Capacity, ct);
        if (!ok)
        {
            return FromError(code!, message);
        }

        return StatusCode(StatusCodes.Status201Created, ToDetailResponse(value!));
    }

    [HttpGet]
    public async Task<IActionResult> Catalog([FromQuery] string? q, CancellationToken ct)
    {
        var (ok, code, message, value) = await rooms.ListCatalogAsync(current.Id, q, ct);
        if (!ok)
        {
            return FromError(code!, message);
        }

        return Ok(value!.Select(ToCatalogEntry).ToList());
    }

    [HttpGet("mine")]
    public async Task<IActionResult> Mine(CancellationToken ct)
    {
        var items = await rooms.ListMineAsync(current.Id, ct);
        return Ok(items.Select(ToMyRoomEntry).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var (ok, code, message, value) = await rooms.GetAsync(current.Id, id, ct);
        if (!ok)
        {
            return FromError(code!, message);
        }

        return Ok(ToDetailResponse(value!));
    }

    [HttpPost("{id:guid}/join")]
    public async Task<IActionResult> Join(Guid id, CancellationToken ct)
    {
        var (ok, code, message, value) = await rooms.JoinAsync(current.Id, id, ct);
        if (!ok)
        {
            return FromError(code!, message);
        }

        return Ok(ToDetailResponse(value!));
    }

    [HttpPost("{id:guid}/leave")]
    public async Task<IActionResult> Leave(Guid id, CancellationToken ct)
    {
        var (ok, code, message) = await rooms.LeaveAsync(current.Id, id, ct);
        if (!ok)
        {
            return FromError(code!, message);
        }

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var (ok, code, message) = await rooms.DeleteAsync(current.Id, id, ct);
        if (!ok)
        {
            return FromError(code!, message);
        }

        return NoContent();
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRoomRequest body, CancellationToken ct)
    {
        var (ok, code, message, value) = await rooms.UpdateAsync(current.Id, id, body.Name, body.Description, body.Visibility, ct);
        if (!ok)
        {
            return FromError(code!, message);
        }

        return Ok(ToDetailResponse(value!));
    }

    [HttpPatch("{id:guid}/capacity")]
    public async Task<IActionResult> UpdateCapacity(Guid id, [FromBody] UpdateCapacityRequest body, CancellationToken ct)
    {
        var (ok, code, message, value) = await rooms.UpdateCapacityAsync(current.Id, id, body.Capacity, ct);
        if (!ok)
        {
            return FromError(code!, message);
        }

        return Ok(ToDetailResponse(value!));
    }

    [HttpPost("{id:guid}/logo")]
    [RequestSizeLimit(1_048_576)]
    [RequestFormLimits(MultipartBodyLengthLimit = 1_048_576)]
    public async Task<IActionResult> UploadLogo(Guid id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "No file provided.",
                extensions: new Dictionary<string, object?> { ["code"] = "no_file" });
        }

        await using var stream = file.OpenReadStream();
        var (ok, code, message, value) = await rooms.SetLogoAsync(current.Id, id, stream, _files.Root, ct);
        if (!ok)
        {
            if (code == "unsupported_media_type")
            {
                return Problem(statusCode: StatusCodes.Status415UnsupportedMediaType,
                    title: message ?? code,
                    extensions: new Dictionary<string, object?> { ["code"] = code });
            }
            if (code == "invalid_image")
            {
                return Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: message ?? code,
                    extensions: new Dictionary<string, object?> { ["code"] = code });
            }
            return FromError(code!, message);
        }
        return Ok(ToDetailResponse(value!));
    }

    [HttpDelete("{id:guid}/logo")]
    public async Task<IActionResult> DeleteLogo(Guid id, CancellationToken ct)
    {
        var (ok, code, message, value) = await rooms.ClearLogoAsync(current.Id, id, _files.Root, ct);
        if (!ok)
        {
            return FromError(code!, message);
        }
        return Ok(ToDetailResponse(value!));
    }

    [HttpGet("{id:guid}/logo")]
    public async Task<IActionResult> GetLogo(Guid id, CancellationToken ct)
    {
        var (logoPath, found) = await rooms.GetLogoPathAsync(id, ct);
        if (!found || logoPath is null)
        {
            return NotFound();
        }

        var filesRoot = Path.GetFullPath(_files.Root);
        var filePath = Path.GetFullPath(Path.Combine(filesRoot, logoPath));

        if (!filePath.StartsWith(filesRoot, StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        if (!System.IO.File.Exists(filePath))
        {
            return NotFound();
        }

        var fileInfo = new FileInfo(filePath);
        var etag = $"\"{id}:{fileInfo.LastWriteTimeUtc.Ticks}\"";

        if (Request.Headers.IfNoneMatch == etag)
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        Response.Headers.ETag = etag;
        Response.Headers.CacheControl = "private, max-age=300";
        Response.Headers.Append("X-Content-Type-Options", "nosniff");

        return PhysicalFile(filePath, "image/webp");
    }

    private static CatalogEntry ToCatalogEntry(CatalogItemOutcome i) =>
        new(i.Id, i.Name, i.Description, i.Visibility.ToString().ToLowerInvariant(),
            i.MemberCount, i.Capacity, i.CreatedAt, i.IsMember, i.LogoUrl);

    private static MyRoomEntry ToMyRoomEntry(MyRoomItemOutcome i) =>
        new(i.Id, i.Name, i.Description, i.Visibility.ToString().ToLowerInvariant(),
            i.MemberCount, i.Capacity, i.CreatedAt,
            i.Role.ToString().ToLowerInvariant(), i.JoinedAt, i.LogoUrl);

    private static RoomDetailResponse ToDetailResponse(RoomDetailOutcome o) =>
        RoomsMappings.ToDetailResponse(o);

    private IActionResult FromError(string code, string? message) =>
        RoomsErrorMapper.FromError(this, code, message);
}
