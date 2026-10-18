namespace CapybaraHome.Domain.Users;

/// <summary>A person who may sign in. Everyone shares the same task data.</summary>
public class AppUser
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Issuer of the external identity, taken from the OpenID Connect provider.</summary>
    public required string Issuer { get; set; }

    /// <summary>Stable subject claim from the provider, unique within <see cref="Issuer"/>.</summary>
    public required string Subject { get; set; }

    /// <summary>Email address reported by the provider, lower-cased.</summary>
    public required string Email { get; set; }

    /// <summary>Name to show in the UI.</summary>
    public required string DisplayName { get; set; }

    /// <summary>True for the account that claimed the empty installation.</summary>
    public bool IsFirstUser { get; set; }

    /// <summary>User who invited this one, or null for the first user.</summary>
    public int? InvitedByUserId { get; set; }

    /// <summary>When the row was created, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the user last signed in, in UTC.</summary>
    public DateTimeOffset? LastSignInAt { get; set; }
}
