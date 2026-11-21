namespace CapybaraHome.Domain.Users;

/// <summary>An access or refresh token issued to an OAuth client for one account.</summary>
public class OAuthToken
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Whether this is an access token or a refresh token.</summary>
    public OAuthTokenKind Kind { get; set; }

    /// <summary>SHA-256 of the token, Base64 encoded. The token itself is never stored.</summary>
    public required string SecretHash { get; set; }

    /// <summary>The client the token was issued to.</summary>
    public int ClientId { get; set; }

    /// <summary>The account the token acts as.</summary>
    public int UserId { get; set; }

    /// <summary>Scope the token carries, or null when none was granted.</summary>
    public string? Scope { get; set; }

    /// <summary>When the token was issued, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the token stops being accepted, in UTC.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>When the token was last accepted, or null.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>When the token was revoked, or null while it is usable.</summary>
    public DateTimeOffset? RevokedAt { get; set; }
}
