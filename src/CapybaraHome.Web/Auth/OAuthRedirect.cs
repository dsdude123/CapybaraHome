namespace CapybaraHome.Web.Auth;

/// <summary>Builds the URLs a client is sent back to once a person has decided.</summary>
public static class OAuthRedirect
{
    /// <summary>The redirect that hands over an authorization code.</summary>
    /// <param name="redirectUri">The client's registered redirect URI.</param>
    /// <param name="code">The code that was just issued.</param>
    /// <param name="state">The state the client sent, echoed back, or null.</param>
    public static string Code(string redirectUri, string code, string? state) =>
        With(redirectUri, [("code", code), ("state", state)]);

    /// <summary>The redirect that reports a refusal, as RFC 6749 names it.</summary>
    /// <param name="redirectUri">The client's registered redirect URI.</param>
    /// <param name="error">The error code.</param>
    /// <param name="description">What went wrong, in words.</param>
    /// <param name="state">The state the client sent, echoed back, or null.</param>
    public static string Error(string redirectUri, string error, string description, string? state) =>
        With(redirectUri, [("error", error), ("error_description", description), ("state", state)]);

    private static string With(string redirectUri, (string Name, string? Value)[] parameters)
    {
        var separator = redirectUri.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var query = string.Join(
            '&',
            parameters
                .Where(p => !string.IsNullOrEmpty(p.Value))
                .Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value!)}"));

        return query.Length == 0 ? redirectUri : $"{redirectUri}{separator}{query}";
    }
}
