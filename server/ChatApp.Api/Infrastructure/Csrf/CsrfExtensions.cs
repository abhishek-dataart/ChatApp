namespace ChatApp.Api.Infrastructure.Csrf;

public static class CsrfExtensions
{
    public static IApplicationBuilder UseCsrf(this IApplicationBuilder app)
        => app.UseMiddleware<CsrfMiddleware>();
}
