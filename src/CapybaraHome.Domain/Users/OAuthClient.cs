namespace CapybaraHome.Domain.Users;

/// <summary>
/// An OAuth client that may ask a person to authorize it, such as a Claude custom connector.
/// Registered dynamically by the client itself or written in by hand.
/// </summary>
public class OAuthClient
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Identifier the client presents at the authorize and token endpoints.</summary>
    public required string ClientId { get; set; }

    /// <summary>Name the client gave for itself, shown on the consent screen.</summary>
    public required string ClientName { get; set; }

    /// <summary>SHA-256 of the client secret, or null for a public client that relies on PKCE.</summary>
    public string? SecretHash { get; set; }

    /// <summary>The redirect URIs this client may be sent back to, one per line.</summary>
    public required string RedirectUris { get; set; }

    /// <summary>How the client authenticates at the token endpoint, as RFC 7591 names it.</summary>
    public required string TokenEndpointAuthMethod { get; set; }

    /// <summary>Whether the client registered itself through the registration endpoint.</summary>
    public bool RegisteredDynamically { get; set; }

    /// <summary>When the client was registered, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When a token was last issued to this client, or null.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>When the client was revoked, or null while it is usable.</summary>
    public DateTimeOffset? RevokedAt { get; set; }
}
