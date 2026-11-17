namespace CapybaraHome.Domain.Users;

/// <summary>
/// One authorization code, written when a person approves a client and redeemed once at the
/// token endpoint. Only a hash of the code is kept, as with every other secret here.
/// </summary>
public class OAuthAuthorizationCode
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>SHA-256 of the code, Base64 encoded.</summary>
    public required string CodeHash { get; set; }

    /// <summary>The client the code was issued to.</summary>
    public int ClientId { get; set; }

    /// <summary>The account that approved it.</summary>
    public int UserId { get; set; }

    /// <summary>The redirect URI the authorize request carried, which the redemption must repeat.</summary>
    public required string RedirectUri { get; set; }

    /// <summary>The PKCE challenge the verifier is checked against.</summary>
    public required string CodeChallenge { get; set; }

    /// <summary>Scope granted, or null when the client asked for none.</summary>
    public string? Scope { get; set; }

    /// <summary>The resource indicator the client asked for, kept so the token can repeat it.</summary>
    public string? Resource { get; set; }

    /// <summary>When the code was issued, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the code stops being redeemable, in UTC.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>When the code was redeemed, or null while it is unused.</summary>
    public DateTimeOffset? RedeemedAt { get; set; }
}
