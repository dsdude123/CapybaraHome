namespace CapybaraHome.Application.Users;

/// <summary>Settings for the connector sign-in, bound from the <c>OAuth</c> configuration section.</summary>
public class OAuthOptions
{
    /// <summary>Configuration section these settings are bound from.</summary>
    public const string SectionName = "OAuth";

    /// <summary>
    /// The callbacks a client may register, which is what stops anyone who can reach this
    /// installation from registering a client called "Claude" that points at an address of
    /// their own. What is compared is the scheme, host and port, not the path, so a connector
    /// that moves its callback path keeps working.
    /// </summary>
    public static readonly string[] DefaultAllowedRedirectUris =
    [
        "https://claude.ai/api/mcp/auth_callback",
        "https://claude.com/api/mcp/auth_callback",
    ];

    /// <summary>
    /// The address clients reach this installation at, which every OAuth document has to agree
    /// on. Empty means the documents follow the request, which is right behind a proxy that
    /// passes the original host on.
    /// </summary>
    public string? Issuer { get; set; }

    /// <summary>
    /// Where a person's browser is sent to approve a client, when that is not the issuer's own
    /// <c>/oauth/authorize</c>. The token, registration and discovery endpoints are called by the
    /// client's servers and have to be public, but the consent page is visited only by the person
    /// approving it, so it can stay on an address only they reach — which keeps the sign-in page
    /// off the internet. Empty means the issuer's own.
    /// </summary>
    public string? AuthorizationEndpoint { get; set; }

    /// <summary>
    /// Callbacks a registration may name, replacing <see cref="DefaultAllowedRedirectUris"/>
    /// when it is set. Each entry stands for its whole origin.
    /// </summary>
    public string[]? AllowedRedirectUris { get; set; }

    /// <summary>The callbacks in force, which is the default list until one is configured.</summary>
    public IReadOnlyList<string> AllowedRedirects()
    {
        // Blank entries are dropped first: a compose file that names the variable without a
        // value binds one empty string, and taking that as the list would refuse every
        // registration instead of falling back to the default.
        var configured = (AllowedRedirectUris ?? [])
            .Where(uri => !string.IsNullOrWhiteSpace(uri))
            .Select(uri => uri.Trim())
            .ToArray();

        return configured.Length > 0 ? configured : DefaultAllowedRedirectUris;
    }
}
