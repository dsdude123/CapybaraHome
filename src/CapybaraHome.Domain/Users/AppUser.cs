namespace CapybaraHome.Domain.Users;

/// <summary>A person who may sign in. Everyone shares the same task data.</summary>
public class AppUser
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Name typed at sign-in, unique and held lower-cased.</summary>
    public required string Username { get; set; }

    /// <summary>Name to show in the UI.</summary>
    public required string DisplayName { get; set; }

    /// <summary>Password as a PBKDF2 hash with its salt and iteration count. Never the password itself.</summary>
    public required string PasswordHash { get; set; }

    /// <summary>Shared secret for the authenticator app, or null when the user has not set one up.</summary>
    public string? TotpSecret { get; set; }

    /// <summary>When the user proved the authenticator works, after which codes are required.</summary>
    public DateTimeOffset? TotpConfirmedAt { get; set; }

    /// <summary>True for the account that claimed the empty installation. It may add and remove users.</summary>
    public bool IsFirstUser { get; set; }

    /// <summary>User who created this one, or null for the first user.</summary>
    public int? CreatedByUserId { get; set; }

    /// <summary>When the row was created, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the user last signed in, in UTC.</summary>
    public DateTimeOffset? LastSignInAt { get; set; }

    /// <summary>True once an authenticator code is required at sign-in.</summary>
    public bool TotpEnabled => TotpSecret is not null && TotpConfirmedAt is not null;
}
