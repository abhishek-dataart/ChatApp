namespace ChatApp.Api.Infrastructure.Csrf;

public sealed class CsrfMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;

        if (request.Method is "GET" or "HEAD" or "OPTIONS"
            || request.Path.StartsWithSegments("/hub")
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
}
