namespace CapybaraHome.Application.Users;

/// <summary>A user as the settings screen shows them.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Email">Email address reported by the provider.</param>
/// <param name="DisplayName">Name to show.</param>
/// <param name="IsFirstUser">True for the account that claimed the installation.</param>
/// <param name="CreatedAt">When the account was created.</param>
/// <param name="LastSignInAt">When the user last signed in, or null.</param>
public record UserView(int Id, string Email, string DisplayName, bool IsFirstUser, DateTimeOffset CreatedAt, DateTimeOffset? LastSignInAt);

/// <summary>An invite as the settings screen shows it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Email">Email address allowed to sign in.</param>
/// <param name="CreatedAt">When the invite was issued.</param>
/// <param name="AcceptedAt">When it was used, or null.</param>
/// <param name="RevokedAt">When it was withdrawn, or null.</param>
public record InviteView(int Id, string Email, DateTimeOffset CreatedAt, DateTimeOffset? AcceptedAt, DateTimeOffset? RevokedAt);

/// <summary>Why a sign-in was turned away, or that it was accepted.</summary>
public enum SignInOutcome
{
    /// <summary>The identity matched an existing user.</summary>
    ExistingUser,

    /// <summary>The identity claimed an installation that had no users.</summary>
    FirstUser,

    /// <summary>The identity matched an open invite and became a user.</summary>
    Invited,

    /// <summary>The identity is unknown and holds no invite.</summary>
    NotInvited,

    /// <summary>The provider sent no email claim, so no invite could be matched.</summary>
    NoEmail,
}

/// <summary>What happened when an external identity signed in.</summary>
/// <param name="Outcome">Whether the identity was accepted, and on what grounds.</param>
/// <param name="User">The user it signed in as, or null when it was turned away.</param>
public record SignInResult(SignInOutcome Outcome, UserView? User)
{
    /// <summary>True when the identity may use the app.</summary>
    public bool IsAllowed => User is not null;
}
