using System.Threading.RateLimiting;

namespace CapybaraHome.Web.Security;

/// <summary>The windows the app, the API and the MCP endpoint are rate limited in.</summary>
public static class RateLimiting
{
    /// <summary>Name of the policy the MCP endpoint runs under.</summary>
    public const string McpPolicy = "mcp";

    /// <summary>Name of the policy the endpoints under <c>/api</c> run under.</summary>
    public const string ApiPolicy = "api";

    /// <summary>Length of every window, which is also how long a refused caller is told to wait.</summary>
    public static readonly TimeSpan WindowLength = TimeSpan.FromMinutes(1);

    /// <summary>The caller a request is counted against.</summary>
    /// <param name="context">The request being counted.</param>
    public static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>A sliding window of <paramref name="permitLimit"/> requests per <see cref="WindowLength"/>.</summary>
    /// <param name="permitLimit">Requests one caller may make inside the window.</param>
    public static SlidingWindowRateLimiterOptions Window(int permitLimit) => new()
    {
        PermitLimit = permitLimit,
        Window = WindowLength,
        SegmentsPerWindow = 4,
        AutoReplenishment = true,
        QueueLimit = 0,
    };
}
