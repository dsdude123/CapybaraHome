using CapybaraHome.Web.Auth;

namespace CapybaraHome.Web.Mcp;

/// <summary>Names the caller a tool call is logged against.</summary>
public static class McpAudit
{
    /// <summary>Name of the user the request's token or sign-in cookie resolved to.</summary>
    /// <param name="accessor">The accessor holding the request the tool is running under.</param>
    public static string CurrentUser(this IHttpContextAccessor accessor) =>
        accessor.HttpContext?.User.FindFirst(CapybaraClaims.Username)?.Value
        ?? accessor.HttpContext?.User.Identity?.Name
        ?? "unknown";
}
