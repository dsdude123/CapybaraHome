using CapybaraHome.Application.Users;

namespace CapybaraHome.Web.Auth;

/// <summary>Sends a browser to the setup page while the installation has no accounts at all.</summary>
public static class FirstRunSetupMiddleware
{
    private static readonly string[] AlwaysAllowed = ["/setup", "/health", "/Error", "/not-found"];

    /// <summary>Adds the redirect. Static files, the API and the MCP endpoint are left alone.</summary>
    public static IApplicationBuilder UseFirstRunSetup(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (ShouldCheck(context.Request.Path))
            {
                var users = context.RequestServices.GetRequiredService<UserService>();
                var claimed = await users.AnyUsersAsync(context.RequestAborted);

                if (!claimed)
                {
                    context.Response.Redirect("/setup");
                    return;
                }
            }

            await next();
        });
    }

    private static bool ShouldCheck(PathString path)
    {
        if (path.StartsWithSegments("/api")
            || path.StartsWithSegments("/mcp")
            || path.StartsWithSegments("/_framework")
            || path.StartsWithSegments("/_blazor")
            || path.StartsWithSegments("/_content"))
        {
            return false;
        }

        if (AlwaysAllowed.Any(allowed => path.StartsWithSegments(allowed, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // A request for a file, such as the stylesheet or the icon, is not a page to redirect.
        return !Path.HasExtension(path.Value);
    }
}
