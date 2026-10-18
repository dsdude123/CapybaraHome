namespace CapybaraHome.Domain.Users;

/// <summary>A bearer token that lets the connector reach the API without a browser sign-in.</summary>
public class ApiToken
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Name the owner gave the token, so it can be recognised and revoked.</summary>
    public required string Name { get; set; }

    /// <summary>First characters of the secret, kept so a token can be identified in a list.</summary>
    public required string Prefix { get; set; }

    /// <summary>SHA-256 of the secret, Base64 encoded. The secret itself is never stored.</summary>
    public required string SecretHash { get; set; }

    /// <summary>User the token acts as.</summary>
    public int UserId { get; set; }

    /// <summary>When the token was minted, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the token was last accepted, in UTC.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>When the token was revoked, or null while it is usable.</summary>
    public DateTimeOffset? RevokedAt { get; set; }
}
