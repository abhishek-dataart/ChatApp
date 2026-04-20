namespace ChatApp.Api.Infrastructure.Csrf;

public sealed class CsrfMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;

        if (request.Path.StartsWithSegments("/hub"))
        {
            // SignalR upgrades/negotiates over GET/POST and the browser doesn't let us attach
            // an X-Csrf-Token to the WebSocket handshake — so instead verify the Origin matches
            // the request's own Host on every hub request. Cookie + Origin check defends against
            // cross-site WebSocket hijacking even if a future browser weakens SameSite=Lax.
            if (!OriginMatchesHost(request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("""{"error":"invalid_origin"}""");
                return;
            }
            await next(context);
            return;
        }

        if (request.Method is "GET" or "HEAD" or "OPTIONS"
            || request.Path.StartsWithSegments("/health"))
        {
            await next(context);
            return;
        }

        var cookieToken = request.Cookies["csrf_token"];

        if (string.IsNullOrEmpty(cookieToken))
        {
            // Unauthenticated request — no CSRF token issued yet; SameSite=Lax covers this.
            await next(context);
            return;
        }

        var headerToken = request.Headers["X-Csrf-Token"].ToString();

        if (!string.Equals(cookieToken, headerToken, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("""{"error":"invalid_csrf_token"}""");
            return;
        }

        await next(context);
    }

    private static bool OriginMatchesHost(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
        {
            // Browsers always send Origin on WebSocket handshakes; a missing Origin on a /hub
            // request means a non-browser client (e.g. native) — cookie auth still gates access.
            return true;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
        {
            return false;
        }

        var host = request.Host.HasValue ? request.Host.Value : null;
        if (string.IsNullOrEmpty(host))
        {
            return false;
        }

        var originHost = originUri.IsDefaultPort
            ? originUri.Host
            : $"{originUri.Host}:{originUri.Port}";
        return string.Equals(originHost, host, StringComparison.OrdinalIgnoreCase);
    }
}
