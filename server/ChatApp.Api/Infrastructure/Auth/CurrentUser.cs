using System.Security.Claims;
using ChatApp.Domain.Abstractions;

namespace ChatApp.Api.Infrastructure.Auth;

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    public bool IsAuthenticated => _accessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;

    public Guid Id
    {
        get
        {
            EnsureAuthenticated();
            var raw = _accessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new UnauthorizedAccessException("Missing user id claim.");
            return Guid.Parse(raw);
        }
    }

    public string Username
    {
        get
        {
            EnsureAuthenticated();
            return _accessor.HttpContext!.User.FindFirstValue(ClaimTypes.Name)
                ?? throw new UnauthorizedAccessException("Missing username claim.");
        }
    }

    public Guid SessionId
    {
        get
        {
            EnsureAuthenticated();
            var raw = _accessor.HttpContext!.User.FindFirstValue(SessionAuthenticationHandler.SessionIdClaim)
                ?? throw new UnauthorizedAccessException("Missing session id claim.");
            return Guid.Parse(raw);
        }
    }

    private void EnsureAuthenticated()
    {
        if (!IsAuthenticated)
        {
            throw new UnauthorizedAccessException("Request is not authenticated.");
        }
    }
}
