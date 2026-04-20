using ChatApp.Api.Contracts.Messages;
using ChatApp.Data.Services.Messaging;
using ChatApp.Domain.Abstractions;
using ChatApp.Domain.Services.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChatApp.Api.Controllers.Messages;

[ApiController, Route("api/messages"), Authorize]
[EnableRateLimiting("messages")]
public sealed class MessagesController(
    MessageService messages,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, [FromBody] EditMessageRequest request, CancellationToken ct)
    {
        var (ok, code, _, value) = await messages.EditAsync(currentUser.Id, id, request.Body, ct);
        if (!ok)
        {
            return code switch
            {
                MessagingErrors.MessageNotFound => NotFound(),
                MessagingErrors.NotAuthor => Forbid(),
                MessagingErrors.BodyEmpty => UnprocessableEntity(new { code }),
                MessagingErrors.BodyTooLong => UnprocessableEntity(new { code }),
                MessagingErrors.MessageAlreadyDeleted => Conflict(new { code }),
                _ => StatusCode(500),
            };
        }
        return Ok(MessageResponse.From(value!));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var (ok, code, _) = await messages.DeleteAsync(currentUser.Id, id, ct);
        if (!ok)
        {
            return code switch
            {
                MessagingErrors.MessageNotFound => NotFound(),
                MessagingErrors.NotAuthorized => Forbid(),
                MessagingErrors.MessageAlreadyDeleted => Conflict(new { code }),
                _ => StatusCode(500),
            };
        }
        return NoContent();
    }
}
