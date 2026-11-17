namespace CapybaraHome.Web.Security;

/// <summary>The headers every response carries, whichever endpoint answered.</summary>
public static class SecurityHeaders
{
    /// <summary>Writes the headers on every response that passes through.</summary>
    /// <param name="app">The pipeline the middleware is added to.</param>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["X-XSS-Protection"] = "1; mode=block";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
            await next();
        });
}
