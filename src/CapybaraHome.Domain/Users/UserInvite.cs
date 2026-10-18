namespace CapybaraHome.Domain.Users;

/// <summary>Permission for one email address to sign in and become a user.</summary>
public class UserInvite
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Email address allowed to sign in, lower-cased.</summary>
    public required string Email { get; set; }

    /// <summary>User who issued the invite.</summary>
    public int CreatedByUserId { get; set; }

    /// <summary>When the invite was issued, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the invite was used, or null while it is outstanding.</summary>
    public DateTimeOffset? AcceptedAt { get; set; }

    /// <summary>When the invite was withdrawn, or null while it stands.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>True while the invite can still be used.</summary>
    public bool IsOpen => AcceptedAt is null && RevokedAt is null;
}
