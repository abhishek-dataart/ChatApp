using ChatApp.Api.Contracts.Sessions;
using ChatApp.Api.Infrastructure.Auth;
using ChatApp.Data.Services.Identity;
using ChatApp.Domain.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatApp.Api.Controllers.Sessions;

[ApiController]
[Route("api/sessions")]
[Authorize]
public class SessionsController : ControllerBase
{
    private readonly SessionQueryService _query;
    private readonly SessionRevocationService _revocation;
    private readonly SessionLookupService _lookup;
    private readonly CookieWriter _cookies;
    private readonly ICurrentUser _current;

    public SessionsController(
        SessionQueryService query,
        SessionRevocationService revocation,
        SessionLookupService lookup,
        CookieWriter cookies,
        ICurrentUser current)
    {
        _query = query;
        _revocation = revocation;
        _lookup = lookup;
        _cookies = cookies;
        _current = current;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var sessions = await _query.ListAsync(_current.Id, _current.SessionId, ct);
        var result = sessions.Select(s => new SessionView(s.Id, s.UserAgent, s.Ip, s.CreatedAt, s.LastSeenAt, s.IsCurrent));
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        var cookieHash = await _revocation.RevokeAsync(_current.Id, id, ct);
        if (cookieHash is null)
        {
            return NotFound();
        }

        _lookup.Evict(cookieHash);

        if (id == _current.SessionId)
        {
            _cookies.Clear(Response);
        }

        return NoContent();
    }

    [HttpPost("revoke-others")]
    public async Task<IActionResult> RevokeOthers(CancellationToken ct)
    {
        var hashes = await _revocation.RevokeOthersAsync(_current.Id, _current.SessionId, ct);
        foreach (var h in hashes)
        {
            _lookup.Evict(h);
        }

        return NoContent();
    }
}
