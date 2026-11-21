namespace CapybaraHome.Application.Users;

/// <summary>What a client asks for when it registers itself.</summary>
/// <param name="ClientName">Name the client gives for itself.</param>
/// <param name="RedirectUris">Where the person may be sent back to after approving.</param>
/// <param name="TokenEndpointAuthMethod">How the client will authenticate at the token endpoint.</param>
public record OAuthClientRegistration(
    string? ClientName,
    IReadOnlyList<string> RedirectUris,
    string? TokenEndpointAuthMethod);

/// <summary>A freshly registered client, the one time its secret is readable.</summary>
/// <param name="ClientId">Identifier the client presents from now on.</param>
/// <param name="ClientSecret">The secret, or null for a public client that relies on PKCE.</param>
/// <param name="ClientName">Name the client gave for itself.</param>
/// <param name="RedirectUris">The registered redirect URIs.</param>
/// <param name="TokenEndpointAuthMethod">The agreed token endpoint authentication method.</param>
/// <param name="CreatedAt">When the registration was written.</param>
public record RegisteredOAuthClient(
    string ClientId,
    string? ClientSecret,
    string ClientName,
    IReadOnlyList<string> RedirectUris,
    string TokenEndpointAuthMethod,
    DateTimeOffset CreatedAt);

/// <summary>A client as the settings screen shows it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="ClientId">Identifier the client presents.</param>
/// <param name="ClientName">Name the client gave for itself.</param>
/// <param name="RedirectUris">The registered redirect URIs.</param>
/// <param name="RegisteredDynamically">Whether the client registered itself.</param>
/// <param name="CreatedAt">When it was registered.</param>
/// <param name="LastUsedAt">When it last took a token, or null.</param>
/// <param name="RevokedAt">When it was revoked, or null.</param>
/// <param name="LiveTokens">How many of its tokens are still usable.</param>
public record OAuthClientView(
    int Id,
    string ClientId,
    string ClientName,
    IReadOnlyList<string> RedirectUris,
    bool RegisteredDynamically,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt,
    int LiveTokens);

/// <summary>A client as the consent screen needs it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="ClientId">Identifier the client presents.</param>
/// <param name="ClientName">Name the client gave for itself, shown to the person.</param>
/// <param name="RegisteredDynamically">Whether the client registered itself, and so named itself.</param>
public record OAuthConsentClient(int Id, string ClientId, string ClientName, bool RegisteredDynamically);

/// <summary>An access token with the refresh token that replaces it.</summary>
/// <param name="AccessToken">The token the client sends as a bearer token.</param>
/// <param name="ExpiresInSeconds">How long the access token lasts.</param>
/// <param name="RefreshToken">The token that buys the next access token.</param>
/// <param name="Scope">Scope granted, or null when none was asked for.</param>
public record IssuedOAuthTokens(string AccessToken, int ExpiresInSeconds, string RefreshToken, string? Scope);

/// <summary>An OAuth failure, carrying the error code the response has to name.</summary>
public class OAuthException : Exception
{
    /// <summary>Creates the failure.</summary>
    /// <param name="error">The RFC 6749 or RFC 7591 error code.</param>
    /// <param name="description">What went wrong, in words.</param>
    public OAuthException(string error, string description)
        : base(description)
    {
        Error = error;
    }

    /// <summary>The error code the response names.</summary>
    public string Error { get; }
}
