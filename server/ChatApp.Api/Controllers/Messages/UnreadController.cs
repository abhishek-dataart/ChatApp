using ChatApp.Api.Contracts.Messages;
using ChatApp.Data.Services.Messaging;
using ChatApp.Domain.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatApp.Api.Controllers.Messages;

[ApiController]
[Route("api/chats/unread")]
[Authorize]
public class UnreadController(UnreadService unread, ICurrentUser current) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var items = await unread.GetAllAsync(current.Id, ct);
        var response = items
            .Select(p => new UnreadResponse(p.Scope, p.ScopeId, p.UnreadCount))
            .ToList();
        return Ok(response);
    }
}
